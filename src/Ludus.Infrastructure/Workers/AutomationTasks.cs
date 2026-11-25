using Ludus.Application.Downloads;
using Ludus.Application.Indexers;
using Ludus.Application.Profiles;
using Ludus.Application.Releases;
using Ludus.Domain.Common;
using Ludus.Infrastructure.Downloads;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Persistence;
using Ludus.Infrastructure.Library;
using Ludus.Infrastructure.Operations;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Workers;

public interface IAutomationTask
{
    TaskKind Kind { get; }
    Task RunAsync(CancellationToken cancellationToken);
}

public sealed class LibraryScanTask(LibraryScanner scanner) : IAutomationTask
{
    public TaskKind Kind => TaskKind.LibraryScan;
    public async Task RunAsync(CancellationToken cancellationToken) => await scanner.ScanAsync(null, null, cancellationToken);
}
public sealed class HealthCheckTask(HealthService health) : IAutomationTask
{
    public TaskKind Kind => TaskKind.HealthCheck;
    public async Task RunAsync(CancellationToken cancellationToken) => await health.CheckAsync(cancellationToken);
}
public sealed class BackupTask(BackupService backups) : IAutomationTask
{
    public TaskKind Kind => TaskKind.Backup;
    public async Task RunAsync(CancellationToken cancellationToken) => await backups.CreateAsync(true, cancellationToken);
}

public sealed class DownloadRefreshTask(DownloadCoordinator coordinator) : IAutomationTask
{
    public TaskKind Kind => TaskKind.DownloadRefresh;
    public Task RunAsync(CancellationToken cancellationToken) => coordinator.RefreshAsync(cancellationToken);
}

public sealed class RssSyncTask(IDbContextFactory<AppDbContext> databaseFactory, IIndexerClient client, ReleaseParser parser,
    ReleaseScoringEngine scorer, DownloadCoordinator coordinator, IRealtimePublisher events) : IAutomationTask
{
    public TaskKind Kind => TaskKind.RssSync;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var indexers = await database.Indexers.Where(x => x.EnableRss).OrderBy(x => x.Priority).ToArrayAsync(cancellationToken);
        var games = await database.Games.AsNoTracking().Where(x => x.Monitored && x.Platform.Monitored).Include(x => x.Platform).Include(x => x.Files)
            .Include(x => x.QualityProfile).ThenInclude(x => x.Formats).ThenInclude(x => x.CustomFormat).ThenInclude(x => x.Rules).AsSplitQuery().ToArrayAsync(cancellationToken);
        var blocks = await database.ReleaseBlocks.AsNoTracking().ToArrayAsync(cancellationToken);
        foreach (var indexer in indexers)
        {
            try
            {
                var page = await client.GetRssAsync(indexer, cancellationToken: cancellationToken);
                var parsed = page.Releases.Where(x => x.ParseWarnings.Count == 0 && x.DownloadUrl is not null
                    && (x.Protocol != DownloadProtocol.Torrent || indexer.MinimumSeeders == 0 || x.Seeders >= indexer.MinimumSeeders))
                    .Select(x => (Release: x, Parsed: parser.Parse(x.Title))).ToArray();
                foreach (var game in games)
                {
                    var best = parsed.Select(x => (x.Release, Decision: scorer.Evaluate(x.Parsed, new ReleaseEvaluationContext(game.QualityProfile, game.Platform)
                    {
                        Game = game, ExistingFiles = game.Files.ToArray(), Blocks = blocks, ReleaseFingerprint = x.Release.Fingerprint, Automatic = true
                    }))).Where(x => x.Decision.Accepted).OrderByDescending(x => x.Decision.Score).FirstOrDefault();
                    if (best.Release is not null)
                    {
                        try { await coordinator.GrabAsync(game.Id, best.Release, true, cancellationToken); }
                        catch (DownloadClientException exception)
                        {
                            await events.PublishAsync("healthWarning", new { resource = $"game:{game.Id}", code = exception.Code }, cancellationToken);
                        }
                    }
                }
                indexer.LastRssSyncAtUtc = DateTime.UtcNow;
                indexer.LastRssGuid = page.Releases.FirstOrDefault()?.ReleaseId;
                await database.SaveChangesAsync(cancellationToken);
                await events.PublishAsync("rssUpdated", new { indexerId = indexer.Id, releases = page.Releases.Count }, cancellationToken);
            }
            catch (IndexerException exception)
            {
                await events.PublishAsync("healthWarning", new { resource = $"indexer:{indexer.Id}", code = exception.Code }, cancellationToken);
            }
        }
    }
}

public sealed class RecyclingCleanupTask(IDbContextFactory<AppDbContext> databaseFactory) : IAutomationTask
{
    public TaskKind Kind => TaskKind.RecyclingCleanup;
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var settings = await database.AppSettings.SingleAsync(cancellationToken);
        if (!Directory.Exists(settings.RecyclingBinPath)) return;
        SafePaths.RejectLinks(settings.RecyclingBinPath);
        var cutoff = DateTime.UtcNow.AddDays(-settings.RecyclingRetentionDays);
        foreach (var folder in Directory.EnumerateDirectories(settings.RecyclingBinPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!long.TryParse(Path.GetFileName(folder), out var downloadId)) continue;
            // Uncommitted import journals still need their recovery copies.
            if (!await database.Downloads.AnyAsync(x => x.Id == downloadId && x.Status == DownloadStatus.Imported && x.ImportedAtUtc < cutoff, cancellationToken)) continue;
            SafePaths.DeleteOwnedTree(settings.RecyclingBinPath, folder);
        }
    }
}
