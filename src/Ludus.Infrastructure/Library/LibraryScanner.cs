using Ludus.Application.Downloads;
using Ludus.Application.Profiles;
using Ludus.Application.Releases;
using Ludus.Application.Verification;
using Ludus.Domain.Activity;
using Ludus.Domain.Common;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Library;

public sealed record ScanResult(int Files, int Added, int Relocated, int Missing, int Skipped);

public sealed class LibraryScanner(IDbContextFactory<AppDbContext> factory, ImportGate gate, IFileHasher hasher,
    IDatVerifier verifier, ReleaseParser parser, ReleaseScoringEngine scoring, DescriptorFiles descriptors, IRealtimePublisher events)
{
    public async Task<ScanResult> ScanAsync(long? rootId, long? platformId, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try { return await ScanCoreAsync(rootId, platformId, cancellationToken); }
        finally { gate.Semaphore.Release(); }
    }

    private async Task<ScanResult> ScanCoreAsync(long? rootId, long? platformId, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var roots = await database.RootFolders.Where(x => rootId == null || x.Id == rootId).ToArrayAsync(cancellationToken);
        if (rootId is not null && roots.Length == 0) throw new KeyNotFoundException();
        var platforms = await database.Platforms.ToArrayAsync(cancellationToken);
        var settings = await database.AppSettings.AsNoTracking().SingleAsync(cancellationToken);
        if (platformId is not null && platforms.All(x => x.Id != platformId)) throw new KeyNotFoundException();
        var profiles = await database.QualityProfiles.Include(x => x.Formats).ThenInclude(x => x.CustomFormat).ThenInclude(x => x.Rules).AsSplitQuery().OrderBy(x => x.Id).ToArrayAsync(cancellationToken);
        if (profiles.Length == 0) throw new InvalidOperationException("A quality profile is required for scans.");
        var filesCount = 0; var added = 0; var relocated = 0; var missing = 0; var skipped = 0;
        foreach (var root in roots)
        {
            await events.PublishAsync("scanUpdated", new { rootId = root.Id, status = "Scanning" }, cancellationToken);
            var paths = SafePaths.EnumerateFiles(root.Path, 1000000)
                .Where(x => !Path.GetRelativePath(root.Path, x).Replace('\\', '/').Split('/').Any(part => part.StartsWith(".ludus-", StringComparison.Ordinal))).ToArray();
            var hashCache = new Dictionary<string, FileHashes>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            async Task<FileHashes> Hash(string path)
            {
                if (hashCache.TryGetValue(path, out var cached)) return cached;
                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
                return hashCache[path] = await hasher.ComputeAsync(input, cancellationToken);
            }
            var games = await database.Games.Include(x => x.Files).Include(x => x.Platform).Include(x => x.QualityProfile)
                .ThenInclude(x => x.Formats).ThenInclude(x => x.CustomFormat).ThenInclude(x => x.Rules).AsSplitQuery().Where(x => x.RootFolderId == root.Id).ToListAsync(cancellationToken);
            var knownFiles = games.SelectMany(x => x.Files).ToArray();
            var relativePaths = paths.Select(x => Path.GetRelativePath(root.Path, x).Replace('\\', '/')).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var byPath = knownFiles.GroupBy(x => x.RelativePath, relativePaths.Comparer).ToDictionary(x => x.Key, x => x.ToArray(), relativePaths.Comparer);
            var moved = knownFiles.Where(x => !relativePaths.Contains(x.RelativePath)).ToList();
            var sets = new Dictionary<string, (ParsedRelease Parsed, Guid Group, FileRole Role)>(relativePaths.Comparer);
            foreach (var path in paths.Where(x => Path.GetExtension(x).ToLowerInvariant() is ".cue" or ".gdi"))
            {
                var parsed = parser.Parse(Path.GetFileName(path));
                var group = byPath.GetValueOrDefault(Path.GetRelativePath(root.Path, path).Replace('\\', '/'))?.FirstOrDefault()?.FileGroupId ?? Guid.NewGuid();
                try
                {
                    var tracks = await descriptors.ReferencesAsync(path, root.Path, cancellationToken);
                    sets[path] = (parsed, group, FileRole.Descriptor);
                    foreach (var track in tracks) sets.TryAdd(track, (parsed, group, FileRole.Track));
                }
                catch (ImportException)
                {
                    await events.PublishAsync("healthWarning", new { resource = $"root:{root.Id}", code = "IncompleteDiscSet" }, cancellationToken);
                }
            }
            var folders = new List<(string Path, string Main, Guid Group)>();
            foreach (var path in paths)
            {
                var normalized = path.Replace('\\', '/');
                var marker = normalized.IndexOf("/PS3_GAME/PARAM.SFO", StringComparison.OrdinalIgnoreCase);
                var parent = marker >= 0 ? normalized[..marker] : Path.GetExtension(path).Equals(".rpx", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "code", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(Path.GetDirectoryName(path)) : null;
                if (parent is not null && folders.All(x => x.Path != parent)) folders.Add((parent, path, Guid.NewGuid()));
            }
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root.Path, path).Replace('\\', '/');
                if (byPath.TryGetValue(relative, out var owners) && owners.Length > 1) { skipped++; continue; }
                var existing = owners?.SingleOrDefault();
                var folder = folders.FirstOrDefault(x => SafePaths.IsWithin(x.Path, path));
                var parsed = folder.Path is not null ? parser.Parse(Path.GetFileName(folder.Path)) with { Extension = null }
                    : sets.TryGetValue(path, out var set) ? set.Parsed : parser.Parse(Path.GetFileName(path));
                Platform? platform = existing?.Game.Platform;
                if (platform is null && platformId is not null) platform = platforms.Single(x => x.Id == platformId);
                var candidates = platforms.Where(x => folder.Path is not null ? x.SupportsFolderGames && (folder.Main.EndsWith("PARAM.SFO", StringComparison.OrdinalIgnoreCase) ? x.Slug == "ps3" : x.Slug == "wii-u")
                    : x.AllowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToArray();
                if (platform is null)
                {
                    var assigned = games.Select(x => x.Platform).DistinctBy(x => x.Id).Where(x => candidates.Contains(x)).ToArray();
                    platform = assigned.Length == 1 ? assigned[0] : candidates.Length == 1 ? candidates[0] : null;
                }
                if (platform is null || existing is null && folder.Path is null && !sets.ContainsKey(path)
                    && !platform.AllowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) { skipped++; continue; }
                var hashes = await Hash(path);
                var identical = moved.Where(x => x.Sha1 == hashes.Sha1 && x.Size == hashes.Size && x.Game.PlatformId == platform.Id).ToArray();
                if (existing is null && identical.Length == 1)
                {
                    existing = identical[0];
                    existing.RelativePath = relative;
                    moved.Remove(existing);
                    relocated++;
                    database.History.Add(new HistoryEntry { GameId = existing.GameId, GameTitle = existing.Game.Title, Event = HistoryEvent.ScanMatched, Message = "Relocated file matched by SHA1 and size." });
                }
                if (existing is null)
                {
                    var elsewhere = await database.GameFiles.Include(x => x.Game).ThenInclude(x => x.RootFolder).Include(x => x.Game).ThenInclude(x => x.Files)
                        .Where(x => x.Sha1 == hashes.Sha1 && x.Size == hashes.Size && x.Game.PlatformId == platform.Id && x.Game.RootFolderId != root.Id).ToArrayAsync(cancellationToken);
                    var absent = elsewhere.Where(x => Directory.Exists(x.Game.RootFolder.Path) && x.Game.Files.All(f => !File.Exists(SafePaths.Within(x.Game.RootFolder.Path, f.RelativePath)))).ToArray();
                    if (absent.Length > 0)
                    {
                        if (absent.Length != 1) { skipped++; continue; }
                        var candidate = absent[0];
                        var destinations = new Dictionary<long, string>();
                        foreach (var member in candidate.Game.Files)
                        {
                            var matches = new List<string>();
                            foreach (var possible in paths.Where(x => new FileInfo(x).Length == member.Size))
                                if ((await Hash(possible)).Sha1 == member.Sha1) matches.Add(possible);
                            if (matches.Count != 1) break;
                            destinations[member.Id] = matches[0];
                        }
                        if (destinations.Count != candidate.Game.Files.Count || destinations.Values.Distinct().Count() != destinations.Count) { skipped++; continue; }
                        foreach (var member in candidate.Game.Files)
                        {
                            member.RelativePath = Path.GetRelativePath(root.Path, destinations[member.Id]).Replace('\\', '/');
                            byPath[member.RelativePath] = [member];
                        }
                        candidate.Game.RootFolder = root; candidate.Game.RootFolderId = root.Id;
                        if (!games.Contains(candidate.Game)) games.Add(candidate.Game);
                        relocated += destinations.Count;
                        existing = candidate;
                        database.History.Add(new HistoryEntry { GameId = candidate.GameId, GameTitle = candidate.Game.Title, Event = HistoryEvent.ScanMatched, Message = "Complete file set relocated to another root by SHA1 and size." });
                    }
                }
                if (existing is null)
                {
                    var matches = games.Where(x => x.PlatformId == platform.Id && (parsed.SwitchIdentity is not null && x.BaseTitleId is not null
                        ? parsed.SwitchIdentity.BaseTitleId.Equals(x.BaseTitleId, StringComparison.OrdinalIgnoreCase)
                        : parsed.Serials.Intersect(x.Serials, StringComparer.OrdinalIgnoreCase).Any() || ReleaseParser.NormalizeTitle(x.Title) == ReleaseParser.NormalizeTitle(parsed.Title))).ToArray();
                    if (matches.Length > 1 || parsed.Warnings.Count > 0 || hashes.Size == 0) { skipped++; continue; }
                    var game = matches.SingleOrDefault();
                    if (game is null)
                    {
                        if (parsed.ContentKind != ContentKind.Base || string.IsNullOrWhiteSpace(parsed.Title)) { skipped++; continue; }
                        var profile = profiles.FirstOrDefault(x => x.PlatformId == platform.Id);
                        if (profile is null)
                        {
                            var template = profiles[0];
                            profile = new Ludus.Domain.Profiles.QualityProfile
                            {
                                Name = template.Name, Platform = platform, MinimumScore = template.MinimumScore,
                                AllowUpgrades = template.AllowUpgrades, UpgradeUntilScore = template.UpgradeUntilScore,
                                AllowedRegions = template.AllowedRegions.ToList(), ExcludedRegions = template.ExcludedRegions.ToList(),
                                RejectBeta = template.RejectBeta, RejectDemo = template.RejectDemo, RejectPrototype = template.RejectPrototype,
                                AllowUpdates = template.AllowUpdates, AllowDlc = template.AllowDlc,
                                Formats = template.Formats.Select(x => new Ludus.Domain.Profiles.QualityProfileFormat
                                    { CustomFormatId = x.CustomFormatId, Score = x.Score }).ToList()
                            };
                            database.QualityProfiles.Add(profile);
                            profiles = [.. profiles, profile];
                        }
                        game = new Game { Title = parsed.Title, SortTitle = parsed.Title, Platform = platform, RootFolder = root, QualityProfile = profile,
                            Serials = parsed.Serials.ToList(), BaseTitleId = parsed.SwitchIdentity?.BaseTitleId };
                        database.Games.Add(game);
                        games.Add(game);
                    }
                    var role = folder.Path is not null ? path == folder.Main ? FileRole.Rom : FileRole.Support
                        : sets.TryGetValue(path, out var info) ? info.Role : Path.GetExtension(path).Equals(".m3u", StringComparison.OrdinalIgnoreCase) ? FileRole.Playlist : FileRole.Rom;
                    existing = new GameFile { Game = game, RelativePath = relative, OriginalFileName = Path.GetFileName(path), ReleaseName = parsed.OriginalTitle,
                        ContentKind = parsed.ContentKind, Role = role, FileGroupId = folder.Path is not null ? folder.Group : sets.TryGetValue(path, out var group) ? group.Group : Guid.NewGuid(),
                        DiscNumber = parsed.DiscNumber, TitleId = parsed.SwitchIdentity?.TitleId, Revision = parsed.Revision, Regions = parsed.Regions.ToList(), Languages = parsed.Languages.ToList(),
                        Score = scoring.Evaluate(parsed, new ReleaseEvaluationContext(game.QualityProfile, platform)).Score, ImportedAtUtc = DateTime.UtcNow };
                    game.Files.Add(existing);
                    database.GameFiles.Add(existing);
                    added++;
                }
                var verification = await verifier.VerifyAsync(platform.Id, hashes, Path.GetFileNameWithoutExtension(path), cancellationToken);
                var changed = existing.Sha1 is not null && existing.Sha1 != hashes.Sha1;
                existing.Size = hashes.Size; existing.Crc32 = hashes.Crc32; existing.Md5 = hashes.Md5; existing.Sha1 = hashes.Sha1;
                existing.VerificationStatus = changed && verification.Status == VerificationStatus.Unverified ? VerificationStatus.Modified : verification.Status;
                existing.DatRomId = verification.Matches.Count == 1 ? verification.Matches[0].DatRomId : null;
                existing.LastSeenAtUtc = existing.LastVerifiedAtUtc = DateTime.UtcNow; existing.Missing = false;
                filesCount++;
                if (filesCount % 25 == 0) await events.PublishAsync("scanUpdated", new { rootId = root.Id, status = "Scanning", files = filesCount }, cancellationToken);
            }
            foreach (var file in moved) { file.Missing = true; missing++; }
            foreach (var game in games) game.Status = game.Files.Any(x => !x.Missing && x.ContentKind == ContentKind.Base && x.Role is FileRole.Rom or FileRole.Descriptor)
                && !game.Files.Any(x => x.Missing && x.ContentKind == ContentKind.Base && x.Role != FileRole.Playlist)
                ? GameStatus.Available : await database.Downloads.AnyAsync(x => x.GameId == game.Id && x.Status != DownloadStatus.Imported && x.Status != DownloadStatus.Failed, cancellationToken) ? GameStatus.Downloading : GameStatus.Missing;
            foreach (var game in games)
            {
                var extension = game.Platform.AllowedExtensions.FirstOrDefault(x => x is not ".zip" and not ".7z") ?? ".rom";
                var rendered = ImportPlanner.Render(settings.NamingSchema, game, parser.Parse(game.Title + extension), extension.TrimStart('.'));
                var relative = Path.GetDirectoryName(rendered)?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(relative)) continue;
                var target = SafePaths.Within(root.Path, relative);
                if (settings.CreateEmptyFolders && game.Monitored)
                {
                    if (game.ManagedFolderRelativePath is null && !Directory.Exists(target))
                    {
                        MediaPermissions.CreateDirectory(target, settings);
                        game.ManagedFolderRelativePath = relative;
                    }
                    else if (string.Equals(game.ManagedFolderRelativePath, relative, SafePaths.Comparison) && !Directory.Exists(target))
                    {
                        MediaPermissions.CreateDirectory(target, settings);
                    }
                }
                if (settings.DeleteEmptyFolders && game.ManagedFolderRelativePath is { } managed
                    && (!game.Monitored || !settings.CreateEmptyFolders))
                {
                    var managedPath = SafePaths.Within(root.Path, managed);
                    if (Directory.Exists(managedPath) && !Directory.EnumerateFileSystemEntries(managedPath).Any())
                    {
                        Directory.Delete(managedPath);
                        game.ManagedFolderRelativePath = null;
                    }
                }
            }
            root.LastScanAtUtc = DateTime.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
            await events.PublishAsync("scanUpdated", new { rootId = root.Id, status = "Completed", files = filesCount, added, relocated, missing, skipped }, cancellationToken);
        }
        var rootIds = roots.Select(x => x.Id).ToArray();
        missing = await database.GameFiles.CountAsync(x => x.Missing && rootIds.Contains(x.Game.RootFolderId), cancellationToken);
        return new ScanResult(filesCount, added, relocated, missing, skipped);
    }
}
