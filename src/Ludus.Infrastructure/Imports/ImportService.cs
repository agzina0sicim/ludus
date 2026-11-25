using System.Text;
using System.Text.Json;
using Ludus.Application.Downloads;
using Ludus.Application.Verification;
using Ludus.Application.Profiles;
using Ludus.Domain.Activity;
using Ludus.Domain.Common;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Notifications;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Imports;

public sealed class ImportGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public void Dispose() => Semaphore.Dispose();
}
public sealed record ImportFile(GameFile File, string StageRelative);
public sealed record RecycledFile(long Id, string RelativePath, string RecycleRelative, string Sha1);
public sealed record ImportJournal(long DownloadId, long GameId, string Root, string Stage, string RecyclingRoot,
    IReadOnlyList<ImportFile> Files, IReadOnlyList<RecycledFile> OldFiles, bool Upgrade);

public sealed class ImportService(IDbContextFactory<AppDbContext> databaseFactory, LudusPaths paths, RemotePathResolver resolver,
    ImportPlanner planner, ArchiveExtractor extractor, AtomicFiles files, IFileHasher hasher, IDatVerifier verifier,
    ImportGate gate, IRealtimePublisher events, ReleaseScoringEngine scorer)
{
    public async Task RecoverPendingAsync(CancellationToken cancellationToken)
    {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var downloads = await database.Downloads.AsNoTracking().Where(x => x.Status == DownloadStatus.Importing
            || x.Status == DownloadStatus.Imported && x.CleanupPending).Select(x => x.Id).ToArrayAsync(cancellationToken);
        foreach (var downloadId in downloads)
        {
            if (!File.Exists(Path.Combine(paths.ConfigDirectory, "Imports", $"{downloadId}.json"))) continue;
            try { await ImportAsync(downloadId, "", cancellationToken); }
            catch (Exception exception) when (exception is ImportException or IOException or UnauthorizedAccessException or JsonException)
            {
                await events.PublishAsync("healthWarning", new { resource = $"download:{downloadId}", code = "ImportRecoveryBlocked" }, cancellationToken);
            }
        }
    }

    public async Task ImportAsync(long downloadId, string remoteContentPath, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try { await ImportCoreAsync(downloadId, remoteContentPath, cancellationToken); }
        finally { gate.Semaphore.Release(); }
    }

    private async Task ImportCoreAsync(long downloadId, string remoteContentPath, CancellationToken cancellationToken)
    {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var download = await database.Downloads.Include(x => x.DownloadClient).Include(x => x.Game).ThenInclude(x => x.Platform)
            .Include(x => x.Game).ThenInclude(x => x.RootFolder).Include(x => x.Game).ThenInclude(x => x.Files)
            .Include(x => x.Game).ThenInclude(x => x.QualityProfile).ThenInclude(x => x.Formats).ThenInclude(x => x.CustomFormat).ThenInclude(x => x.Rules).AsSplitQuery()
            .SingleAsync(x => x.Id == downloadId, cancellationToken);
        var journalPath = Path.Combine(paths.ConfigDirectory, "Imports", $"{downloadId}.json");
        ImportJournal journal;
        if (File.Exists(journalPath))
        {
            SafePaths.RejectLinks(journalPath);
            if (new FileInfo(journalPath).Length > 128 * 1024 * 1024) throw new ImportException("InvalidImportJournal", false);
            await using var stream = File.OpenRead(journalPath);
            journal = await JsonSerializer.DeserializeAsync<ImportJournal>(stream, cancellationToken: cancellationToken)
                ?? throw new ImportException("InvalidImportJournal", false);
            if (journal.DownloadId != downloadId || journal.GameId != download.GameId || journal.Root != download.Game.RootFolder.Path
                || !SafePaths.IsWithin(Path.Combine(journal.Root, ".ludus-staging"), journal.Stage)) throw new ImportException("ImportConfigurationChanged", false);
        }
        else
        {
            if (download.Status == DownloadStatus.Imported) return;
            journal = await PrepareAsync(database, download, remoteContentPath, cancellationToken);
            await AtomicFiles.WriteJsonAsync(journalPath, journal, cancellationToken);
        }
        if (download.Status != DownloadStatus.Imported)
        {
            await events.PublishAsync("importStatus", new { downloadId, gameId = download.GameId, status = "Importing" }, cancellationToken);
            await ApplyFilesAsync(journal, cancellationToken);
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            var oldIds = journal.OldFiles.Select(x => x.Id).ToArray();
            database.GameFiles.RemoveRange(download.Game.Files.Where(x => oldIds.Contains(x.Id)));
            await database.SaveChangesAsync(cancellationToken);
            database.GameFiles.AddRange(journal.Files.Select(x => x.File));
            download.Status = DownloadStatus.Imported;
            download.ImportedAtUtc = DateTime.UtcNow;
            download.FailureCode = null;
            download.CleanupPending = true;
            download.Game.Status = download.Game.Files.Where(x => !oldIds.Contains(x.Id)).Concat(journal.Files.Select(x => x.File))
                .Any(x => !x.Missing && x.ContentKind == ContentKind.Base && x.Role is FileRole.Rom or FileRole.Descriptor)
                ? GameStatus.Available : GameStatus.Missing;
            database.History.Add(new HistoryEntry
            {
                GameId = download.GameId, DownloadId = download.Id, GameTitle = download.Game.Title, ReleaseTitle = download.ReleaseTitle,
                Event = journal.Upgrade ? HistoryEvent.Upgraded : HistoryEvent.Imported, ContentKind = download.ContentKind,
                Score = download.Score, Message = journal.Upgrade ? "Upgraded; previous files moved to recycling bin." : "Imported completed download."
            });
            await WebhookService.EnqueueAsync(database, journal.Upgrade ? NotificationEvent.Upgraded : NotificationEvent.Imported,
                download.Game.Title, download.ReleaseTitle, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await events.PublishAsync("importStatus", new { downloadId, gameId = download.GameId, status = "Imported" }, cancellationToken);
        }
        SafePaths.DeleteOwnedTree(Path.Combine(journal.Root, ".ludus-staging"), journal.Stage);
        File.Delete(journalPath);
    }

    private async Task<ImportJournal> PrepareAsync(AppDbContext database, TrackedDownload download, string remotePath, CancellationToken cancellationToken)
    {
        var settings = await database.AppSettings.SingleAsync(cancellationToken);
        var game = download.Game;
        if (!Directory.Exists(game.RootFolder.Path)) throw new ImportException("RootFolderMissing", false);
        SafePaths.RejectLinks(game.RootFolder.Path);
        if (!Path.IsPathFullyQualified(settings.RecyclingBinPath)) throw new ImportException("InvalidRecyclingPath", false);
        var mappings = await database.RemotePathMappings.AsNoTracking().ToListAsync(cancellationToken);
        var resolved = resolver.Resolve(new Uri(download.DownloadClient.BaseUrl).Host, remotePath, mappings);
        if (!File.Exists(resolved.Path) && !Directory.Exists(resolved.Path)) throw new ImportException("ContentPathUnavailable", false);
        if (SafePaths.IsWithin(game.RootFolder.Path, resolved.Path) || SafePaths.IsWithin(resolved.Path, game.RootFolder.Path))
            throw new ImportException("OverlappingDownloadAndLibrary", false);
        var stagingRoot = Path.Combine(game.RootFolder.Path, ".ludus-staging");
        var stage = SafePaths.Within(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var content = resolved.Path;
            if (game.Platform.ExtractArchives)
            {
                var sourceFiles = File.Exists(content) ? new[] { content } : SafePaths.EnumerateFiles(content, settings.MaximumArchiveEntries);
                var archives = sourceFiles.Where(x => Path.GetExtension(x).ToLowerInvariant() is ".zip" or ".7z").ToArray();
                if (archives.Length > 0)
                {
                    var extracted = SafePaths.Within(stage, "extracted");
                    foreach (var archive in archives) await extractor.ExtractAsync(archive, extracted, settings, cancellationToken);
                    foreach (var source in sourceFiles.Except(archives))
                        await files.StageAsync(source, SafePaths.Within(extracted, Path.GetRelativePath(content, source)), settings.PreferHardlinks, cancellationToken);
                    content = extracted;
                }
            }
            var sources = await planner.PlanAsync(download, content, settings, cancellationToken);
            var prepared = new List<ImportFile>();
            foreach (var source in sources)
            {
                var stagedRelative = "files/" + prepared.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var staged = SafePaths.Within(stage, stagedRelative);
                await files.StageAsync(source.Source, staged, settings.PreferHardlinks, cancellationToken);
                var hashes = await HashAsync(staged, cancellationToken);
                if (hashes.Size == 0 && source.Role is FileRole.Rom or FileRole.Descriptor) throw new ImportException("EmptyRom", true);
                var verification = await verifier.VerifyAsync(game.PlatformId, hashes, cancellationToken: cancellationToken);
                var relative = source.RelativeTarget;
                if (settings.UseCanonicalDatNames && settings.RenameFiles && sources.Count == 1 && verification.Matches.Select(x => x.CanonicalName).Distinct().Count() == 1)
                    relative = (Path.GetDirectoryName(relative)?.Replace('\\', '/') is { Length: > 0 } folder ? folder + "/" : "")
                        + SafePaths.Component(verification.Matches[0].CanonicalName) + Path.GetExtension(source.Source).ToLowerInvariant();
                var parsed = source.Parsed;
                var score = download.Score;
                if (source.Role is FileRole.Rom or FileRole.Descriptor)
                {
                    var decision = scorer.Evaluate(parsed, new ReleaseEvaluationContext(game.QualityProfile, game.Platform));
                    if (!decision.Accepted) throw new ImportException("ImportedReleaseRejected", true);
                    score = decision.Score;
                }
                prepared.Add(new ImportFile(new GameFile
                {
                    GameId = game.Id, RelativePath = relative, OriginalFileName = Path.GetFileName(source.Source), ReleaseName = download.ReleaseTitle,
                    FileGroupId = source.GroupId, ContentKind = parsed.ContentKind, Role = source.Role, DiscNumber = parsed.DiscNumber,
                    Score = score, Size = hashes.Size, Crc32 = hashes.Crc32, Md5 = hashes.Md5, Sha1 = hashes.Sha1,
                    TitleId = parsed.SwitchIdentity?.TitleId, Revision = parsed.Revision, Regions = parsed.Regions.ToList(), Languages = parsed.Languages.ToList(),
                    VerificationStatus = verification.Status, DatRomId = verification.Matches.Count == 1 ? verification.Matches[0].DatRomId : null,
                    ImportedAtUtc = DateTime.UtcNow, LastVerifiedAtUtc = DateTime.UtcNow, LastSeenAtUtc = DateTime.UtcNow
                }, stagedRelative));
            }
            var old = game.Files.Where(existing => existing.Role != FileRole.Playlist && prepared.Any(incoming => Replaces(incoming.File, existing))).ToList();
            await AddPlaylistAsync(game, stage, prepared, old, cancellationToken);
            var recycle = new List<RecycledFile>();
            foreach (var existing in old.DistinctBy(x => x.Id))
            {
                var absolute = SafePaths.Within(game.RootFolder.Path, existing.RelativePath);
                var sha1 = File.Exists(absolute) ? (await HashAsync(absolute, cancellationToken)).Sha1 : "";
                recycle.Add(new RecycledFile(existing.Id, existing.RelativePath, $"{download.Id}/{existing.Id}-{SafePaths.Component(Path.GetFileName(existing.RelativePath))}", sha1));
            }
            foreach (var item in prepared)
            {
                var destination = SafePaths.Within(game.RootFolder.Path, item.File.RelativePath);
                if (File.Exists(destination) && !old.Any(x => string.Equals(x.RelativePath, item.File.RelativePath, SafePaths.Comparison)))
                    throw new ImportException("UnmanagedDestinationExists", false);
                if (await database.GameFiles.AnyAsync(x => x.GameId != game.Id && x.Game.RootFolderId == game.RootFolderId && x.RelativePath == item.File.RelativePath, cancellationToken))
                    throw new ImportException("OtherGameOwnsDestination", false);
            }
            return new ImportJournal(download.Id, game.Id, game.RootFolder.Path, stage, settings.RecyclingBinPath, prepared, recycle,
                old.Any(x => x.Role is FileRole.Rom or FileRole.Descriptor));
        }
        catch
        {
            SafePaths.DeleteOwnedTree(stagingRoot, stage);
            throw;
        }
    }

    private static bool Replaces(GameFile incoming, GameFile existing) => incoming.ContentKind == existing.ContentKind
        && (incoming.ContentKind == ContentKind.Base || string.Equals(incoming.TitleId, existing.TitleId, StringComparison.OrdinalIgnoreCase))
        && (incoming.DiscNumber is null || existing.DiscNumber == incoming.DiscNumber);

    private async Task AddPlaylistAsync(Game game, string stage, List<ImportFile> prepared, List<GameFile> old, CancellationToken cancellationToken)
    {
        var candidates = game.Files.Where(x => !x.Missing && !old.Contains(x)).Concat(prepared.Select(x => x.File))
            .Where(x => x.ContentKind == ContentKind.Base && x.DiscNumber is not null && x.Role is FileRole.Rom or FileRole.Descriptor)
            .GroupBy(x => x.DiscNumber).OrderBy(x => x.Key).Select(x => x.OrderByDescending(f => f.Role == FileRole.Descriptor).First()).ToArray();
        if (candidates.Length < 2) return;
        var parent = Path.GetDirectoryName(candidates[0].RelativePath)?.Replace('\\', '/') ?? "";
        var relative = (parent.Length > 0 ? parent + "/" : "") + SafePaths.Component(game.Title) + ".m3u";
        var playlistPath = SafePaths.Within(game.RootFolder.Path, relative);
        var lines = candidates.Select(x => Path.GetRelativePath(Path.GetDirectoryName(playlistPath)!, SafePaths.Within(game.RootFolder.Path, x.RelativePath)).Replace('\\', '/'));
        var stageRelative = "files/playlist";
        await File.WriteAllTextAsync(SafePaths.Within(stage, stageRelative), string.Join('\n', lines) + "\n", new UTF8Encoding(false), cancellationToken);
        var hashes = await HashAsync(SafePaths.Within(stage, stageRelative), cancellationToken);
        prepared.Add(new ImportFile(new GameFile
        {
            GameId = game.Id, RelativePath = relative, OriginalFileName = Path.GetFileName(relative), ReleaseName = game.Title,
            FileGroupId = Guid.NewGuid(), Role = FileRole.Playlist, Size = hashes.Size, Crc32 = hashes.Crc32, Md5 = hashes.Md5, Sha1 = hashes.Sha1,
            ImportedAtUtc = DateTime.UtcNow, LastSeenAtUtc = DateTime.UtcNow
        }, stageRelative));
        old.AddRange(game.Files.Where(x => x.Role == FileRole.Playlist && x.RelativePath == relative));
    }

    private async Task ApplyFilesAsync(ImportJournal journal, CancellationToken cancellationToken)
    {
        foreach (var old in journal.OldFiles)
        {
            var source = SafePaths.Within(journal.Root, old.RelativePath);
            var recycled = SafePaths.Within(journal.RecyclingRoot, old.RecycleRelative);
            if (old.Sha1.Length == 0) continue;
            if (!File.Exists(recycled))
            {
                if (!File.Exists(source) || (await HashAsync(source, cancellationToken)).Sha1 != old.Sha1) throw new ImportException("ExistingFileChanged", false);
                await files.CopyAsync(source, recycled, cancellationToken);
            }
            if ((await HashAsync(recycled, cancellationToken)).Sha1 != old.Sha1) throw new ImportException("RecyclingVerificationFailed", false);
            var replacement = journal.Files.FirstOrDefault(x => string.Equals(x.File.RelativePath, old.RelativePath, SafePaths.Comparison));
            if (replacement is not null && !File.Exists(SafePaths.Within(journal.Stage, replacement.StageRelative))) continue;
            if (File.Exists(source) && (await HashAsync(source, cancellationToken)).Sha1 == old.Sha1) File.Delete(source);
        }
        foreach (var item in journal.Files)
        {
            var staged = SafePaths.Within(journal.Stage, item.StageRelative);
            var target = SafePaths.Within(journal.Root, item.File.RelativePath);
            if (File.Exists(staged))
            {
                if (File.Exists(target)) throw new ImportException("DestinationChanged", false);
                if ((await HashAsync(staged, cancellationToken)).Sha1 != item.File.Sha1) throw new ImportException("StagedFileChanged", false);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(staged, target, false);
            }
            if (!File.Exists(target) || (await HashAsync(target, cancellationToken)).Sha1 != item.File.Sha1) throw new ImportException("PublishedFileChanged", false);
        }
    }

    private async Task<FileHashes> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await hasher.ComputeAsync(input, cancellationToken);
    }
}
