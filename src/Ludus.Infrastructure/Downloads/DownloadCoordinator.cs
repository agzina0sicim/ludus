using Ludus.Application.Downloads;
using Ludus.Application.Indexers;
using Ludus.Application.Profiles;
using Ludus.Application.Releases;
using Ludus.Application.Security;
using Ludus.Domain.Activity;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Notifications;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Downloads;

public sealed class DownloadGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public void Dispose() => Semaphore.Dispose();
}

public sealed class DownloadCoordinator(IDbContextFactory<AppDbContext> databaseFactory, IEnumerable<IDownloadClientAdapter> adapters,
    ISecretProtector secrets, ImportService importer, ReleaseParser parser, ReleaseScoringEngine scorer, IInteractiveSearch search,
    DownloadGate gate, IRealtimePublisher events)
{
    private IDownloadClientAdapter Adapter(DownloadClient client) => adapters.Single(x => x.Kind == client.Kind);

    public async Task<long?> GrabAsync(long gameId, IndexerRelease release, bool automatic, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try { return await GrabCoreAsync(gameId, release, automatic, cancellationToken); }
        finally { gate.Semaphore.Release(); }
    }

    private async Task<long?> GrabCoreAsync(long gameId, IndexerRelease release, bool automatic, CancellationToken cancellationToken)
    {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var game = await database.Games.Include(x => x.Platform).Include(x => x.Files).Include(x => x.QualityProfile)
            .ThenInclude(x => x.Formats).ThenInclude(x => x.CustomFormat).ThenInclude(x => x.Rules).AsSplitQuery()
            .SingleAsync(x => x.Id == gameId, cancellationToken);
        if (await database.Downloads.AnyAsync(x => x.GameId == gameId && x.Status != DownloadStatus.Imported && x.Status != DownloadStatus.Failed, cancellationToken)) return null;
        var indexer = await database.Indexers.SingleAsync(x => x.Id == release.IndexerId, cancellationToken);
        var blocks = await database.ReleaseBlocks.Where(x => x.GameId == gameId).ToArrayAsync(cancellationToken);
        var settings = await database.AppSettings.AsNoTracking().SingleAsync(cancellationToken);
        var parsed = parser.Parse(release.Title);
        var decision = scorer.Evaluate(parsed, new ReleaseEvaluationContext(game.QualityProfile, game.Platform)
        {
            Game = game, ExistingFiles = game.Files.ToArray(), Blocks = blocks, ReleaseFingerprint = release.Fingerprint, Automatic = automatic
        });
        if (!decision.Accepted || ReleasePolicy.Rejections(release, settings, DateTime.UtcNow).Count > 0
            || release.DownloadUrl is null || release.ParseWarnings.Count > 0
            || release.Protocol == DownloadProtocol.Torrent && indexer.MinimumSeeders > 0 && (release.Seeders is null || release.Seeders < indexer.MinimumSeeders))
            throw new DownloadClientException("ReleaseRejected");
        var client = await database.DownloadClients.Where(x => x.Enabled && (release.Protocol == DownloadProtocol.Usenet
            ? x.Kind == DownloadClientKind.Sabnzbd : x.Kind != DownloadClientKind.Sabnzbd)).OrderBy(x => x.Priority).ThenBy(x => x.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new DownloadClientException("NoCompatibleDownloadClient");
        var token = "ludus-" + Guid.NewGuid().ToString("N");
        var download = new TrackedDownload
        {
            GameId = game.Id, DownloadClientId = client.Id, IndexerId = indexer.Id, DownloadId = token, SubmissionToken = token,
            ReleaseGuid = release.ReleaseId, ReleaseFingerprint = release.Fingerprint, ReleaseTitle = release.Title, SourceUrl = release.DownloadUrl,
            Protocol = release.Protocol, ContentKind = parsed.ContentKind, Status = DownloadStatus.Queued, Score = decision.Score, Size = release.Size,
            FailureCode = "SubmissionPending", Origin = automatic ? GrabOrigin.Automatic : GrabOrigin.Interactive
        };
        database.Downloads.Add(download);
        game.Status = GameStatus.Downloading;
        await database.SaveChangesAsync(cancellationToken);
        try
        {
            var id = await Adapter(client).AddAsync(client, secrets.Unprotect(release.DownloadUrl), release.Title, token, cancellationToken);
            if (!string.IsNullOrWhiteSpace(id))
            {
                download.DownloadId = id;
                await ConfirmGrabAsync(database, download, cancellationToken);
            }
        }
        catch (DownloadClientException exception)
        {
            download.FailureCode = "SubmissionUnconfirmed";
            await database.SaveChangesAsync(cancellationToken);
            await events.PublishAsync("healthWarning", new { resource = $"download:{download.Id}", code = exception.Code }, cancellationToken);
        }
        await PublishAsync(download, cancellationToken);
        return download.Id;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            await importer.RecoverPendingAsync(cancellationToken);
            await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
            var clients = await database.DownloadClients.AsNoTracking().Where(x => x.Enabled).ToArrayAsync(cancellationToken);
            foreach (var client in clients)
            {
                IReadOnlyList<ClientDownload> snapshots;
                try { snapshots = await Adapter(client).GetDownloadsAsync(client, cancellationToken); }
                catch (DownloadClientException exception)
                {
                    await events.PublishAsync("healthWarning", new { resource = $"client:{client.Id}", code = exception.Code }, cancellationToken);
                    continue;
                }
                var tracked = await database.Downloads.Include(x => x.Game).Where(x => x.DownloadClientId == client.Id
                    && (x.Status != DownloadStatus.Imported && x.Status != DownloadStatus.Failed || x.CleanupPending || x.ReplacementSearchPending)).ToListAsync(cancellationToken);
                foreach (var download in tracked)
                {
                    var snapshot = snapshots.FirstOrDefault(x => x.Id == download.DownloadId || download.SubmissionToken != null && x.Labels.Contains(download.SubmissionToken));
                    if (download.FailureCode is "SubmissionPending" or "SubmissionUnconfirmed")
                    {
                        if (snapshot is null)
                        {
                            await events.PublishAsync("healthWarning", new { resource = $"download:{download.Id}", code = "SubmissionNeedsReconciliation" }, cancellationToken);
                            continue;
                        }
                        download.DownloadId = snapshot.Id;
                        await ConfirmGrabAsync(database, download, cancellationToken);
                    }
                    if (snapshot is null)
                    {
                        if (download.CleanupPending) { download.CleanupPending = false; await database.SaveChangesAsync(cancellationToken); }
                        if (download.Status is not (DownloadStatus.Imported or DownloadStatus.Failed))
                            await events.PublishAsync("healthWarning", new { resource = $"download:{download.Id}", code = "DownloadMissingFromClient" }, cancellationToken);
                        continue;
                    }
                    if (download.Status is DownloadStatus.Imported or DownloadStatus.Failed)
                    {
                        await CleanupAsync(database, client, download, snapshot, cancellationToken);
                        continue;
                    }
                    var elapsed = (DateTime.UtcNow - download.UpdatedAtUtc).TotalSeconds;
                    var measuredSpeed = snapshot.Status == DownloadStatus.Downloading && elapsed > 0
                        ? (long)Math.Max(0, (snapshot.DownloadedBytes - download.DownloadedBytes) / elapsed) : 0;
                    download.Size = snapshot.Size;
                    download.DownloadedBytes = snapshot.DownloadedBytes;
                    download.SpeedBytesPerSecond = snapshot.SpeedBytesPerSecond > 0 ? snapshot.SpeedBytesPerSecond : measuredSpeed;
                    download.EtaSeconds = snapshot.EtaSeconds ?? (download.SpeedBytesPerSecond > 0 ? Math.Max(0, snapshot.Size - snapshot.DownloadedBytes) / download.SpeedBytesPerSecond : null);
                    download.RemoteContentPath = snapshot.ContentPath;
                    if (snapshot.FailureKind is ClientFailureKind.Temporary or ClientFailureKind.InvalidContent)
                    {
                        await FailAsync(database, download, snapshot.FailureKind == ClientFailureKind.InvalidContent, snapshot.FailureCode ?? "DownloadFailed", client, cancellationToken);
                        await CleanupAsync(database, client, download, snapshot, cancellationToken);
                    }
                    else if (snapshot.FailureKind == ClientFailureKind.LocalConfiguration)
                    {
                        download.FailureCode = snapshot.FailureCode;
                        await database.SaveChangesAsync(cancellationToken);
                        await events.PublishAsync("healthWarning", new { resource = $"download:{download.Id}", code = snapshot.FailureCode }, cancellationToken);
                    }
                    else if (snapshot.Status == DownloadStatus.Completed)
                    {
                        var settings = await database.AppSettings.AsNoTracking().SingleAsync(cancellationToken);
                        if (!settings.EnableCompletedDownloadHandling)
                        {
                            download.Status = DownloadStatus.Completed;
                            download.CompletedAtUtc ??= DateTime.UtcNow;
                            download.FailureCode = null;
                            await database.SaveChangesAsync(cancellationToken);
                            await PublishAsync(download, cancellationToken);
                            continue;
                        }
                        download.Status = DownloadStatus.Importing;
                        download.CompletedAtUtc ??= DateTime.UtcNow;
                        download.ImportAttempts++;
                        await database.SaveChangesAsync(cancellationToken);
                        try
                        {
                            await importer.ImportAsync(download.Id, snapshot.ContentPath ?? "", cancellationToken);
                            await database.Entry(download).ReloadAsync(cancellationToken);
                            await CleanupAsync(database, client, download, snapshot, cancellationToken);
                        }
                        catch (ImportException exception) when (exception.Permanent)
                        {
                            await FailAsync(database, download, true, exception.Code, client, cancellationToken, importFailure: true);
                            await CleanupAsync(database, client, download, snapshot, cancellationToken);
                        }
                        catch (Exception exception) when (exception is ImportException or IOException or UnauthorizedAccessException)
                        {
                            var failureCode = exception is ImportException error ? error.Code : "ImportStorageUnavailable";
                            if (download.FailureCode != failureCode)
                                await WebhookService.EnqueueAsync(database, NotificationEvent.ImportFailed, download.Game.Title, download.ReleaseTitle, cancellationToken);
                            download.FailureCode = failureCode;
                            await database.SaveChangesAsync(cancellationToken);
                            await events.PublishAsync("healthWarning", new { resource = $"download:{download.Id}", code = download.FailureCode }, cancellationToken);
                        }
                    }
                    else
                    {
                        download.Status = snapshot.Status;
                        download.FailureCode = null;
                        await database.SaveChangesAsync(cancellationToken);
                    }
                    await PublishAsync(download, cancellationToken);
                }
                database.ChangeTracker.Clear();
            }
            var replacements = await database.Downloads.Where(x => x.ReplacementSearchPending).ToListAsync(cancellationToken);
            foreach (var failed in replacements)
            {
                var results = await search.SearchAsync(failed.GameId, cancellationToken, automatic: true);
                var best = results.Releases.FirstOrDefault(x => x.Decision.Accepted);
                if (best is not null) await GrabCoreAsync(failed.GameId, best.Release, true, cancellationToken);
                if (results.Failures.Count == 0 || best is not null)
                {
                    failed.ReplacementSearchPending = false;
                    await database.SaveChangesAsync(cancellationToken);
                }
            }
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task ImportCompletedAsync(long downloadId, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
            var download = await database.Downloads.Include(x => x.DownloadClient).Include(x => x.Game)
                .SingleOrDefaultAsync(x => x.Id == downloadId, cancellationToken) ?? throw new KeyNotFoundException();
            if (download.Status != DownloadStatus.Completed || string.IsNullOrWhiteSpace(download.RemoteContentPath))
                throw new InvalidOperationException("Only completed downloads with a content path can be imported manually.");
            download.Status = DownloadStatus.Importing;
            download.ImportAttempts++;
            await database.SaveChangesAsync(cancellationToken);
            try { await importer.ImportAsync(download.Id, download.RemoteContentPath, cancellationToken); }
            catch (ImportException exception) when (exception.Permanent)
            {
                await FailAsync(database, download, true, exception.Code, download.DownloadClient, cancellationToken, importFailure: true);
                throw;
            }
        }
        finally { gate.Semaphore.Release(); }
    }

    private static async Task ConfirmGrabAsync(AppDbContext database, TrackedDownload download, CancellationToken cancellationToken)
    {
        download.FailureCode = null;
        if (!await database.History.AnyAsync(x => x.DownloadId == download.Id && x.Event == HistoryEvent.Grabbed, cancellationToken))
        {
            database.History.Add(new HistoryEntry { GameId = download.GameId, DownloadId = download.Id, GameTitle = download.Game.Title,
                ReleaseTitle = download.ReleaseTitle, Event = HistoryEvent.Grabbed, Score = download.Score, ContentKind = download.ContentKind, Message = "Release accepted by download client." });
            await WebhookService.EnqueueAsync(database, NotificationEvent.Grabbed, download.Game.Title, download.ReleaseTitle, cancellationToken);
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    private static async Task FailAsync(AppDbContext database, TrackedDownload download, bool permanent, string code, DownloadClient client,
        CancellationToken cancellationToken, bool importFailure = false)
    {
        if (download.FailureHandledAtUtc is not null) return;
        var block = await database.ReleaseBlocks.SingleOrDefaultAsync(x => x.GameId == download.GameId && x.ReleaseFingerprint == download.ReleaseFingerprint, cancellationToken);
        if (block is null)
        {
            block = new ReleaseBlock { GameId = download.GameId, IndexerId = download.IndexerId, ReleaseFingerprint = download.ReleaseFingerprint,
                ReleaseTitle = download.ReleaseTitle, ReasonCode = code };
            database.ReleaseBlocks.Add(block);
        }
        permanent |= block.Kind == BlockKind.Permanent;
        block.Kind = permanent ? BlockKind.Permanent : BlockKind.Cooldown;
        block.ExpiresAtUtc = permanent ? null : DateTime.UtcNow.AddDays(7);
        block.ReasonCode = code;
        download.Status = DownloadStatus.Failed;
        download.FailureCode = code;
        download.CleanupPending = client.RemoveFailed;
        var settings = await database.AppSettings.AsNoTracking().SingleAsync(cancellationToken);
        download.ReplacementSearchPending = download.Origin == GrabOrigin.Interactive
            ? settings.RedownloadFailedFromInteractiveSearch : settings.RedownloadFailed;
        download.FailureHandledAtUtc = DateTime.UtcNow;
        download.Game.Status = await database.GameFiles.AnyAsync(x => x.GameId == download.GameId && !x.Missing && x.ContentKind == ContentKind.Base, cancellationToken)
            ? GameStatus.Available : GameStatus.Missing;
        database.History.Add(new HistoryEntry { GameId = download.GameId, DownloadId = download.Id, GameTitle = download.Game.Title, ReleaseTitle = download.ReleaseTitle,
            Event = HistoryEvent.DownloadFailed, ContentKind = download.ContentKind, Message = code });
        await WebhookService.EnqueueAsync(database, importFailure ? NotificationEvent.ImportFailed : NotificationEvent.DownloadFailed,
            download.Game.Title, download.ReleaseTitle, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task CleanupAsync(AppDbContext database, DownloadClient client, TrackedDownload download, ClientDownload snapshot, CancellationToken cancellationToken)
    {
        if (!download.CleanupPending) return;
        try
        {
            if (download.Status == DownloadStatus.Imported)
            {
                await Adapter(client).MarkImportedAsync(client, download.DownloadId, cancellationToken);
                if (client.RemoveCompleted && !snapshot.CanRemove) return;
                if (client.RemoveCompleted) await Adapter(client).RemoveAsync(client, download.DownloadId, false, cancellationToken);
            }
            else if (download.Status == DownloadStatus.Failed)
            {
                var permanent = await database.ReleaseBlocks.AnyAsync(x => x.GameId == download.GameId && x.ReleaseFingerprint == download.ReleaseFingerprint && x.Kind == BlockKind.Permanent, cancellationToken);
                await Adapter(client).RemoveAsync(client, download.DownloadId, permanent, cancellationToken);
            }
            download.CleanupPending = false;
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DownloadClientException exception)
        {
            await events.PublishAsync("healthWarning", new { resource = $"download:{download.Id}", code = exception.Code }, cancellationToken);
        }
    }

    private Task PublishAsync(TrackedDownload download, CancellationToken cancellationToken) => events.PublishAsync("queueUpdated",
        new QueueEvent(download.Id, download.GameId, download.Status, download.Size, download.DownloadedBytes, download.SpeedBytesPerSecond, download.EtaSeconds, download.FailureCode), cancellationToken);
}
