using Ludus.Application.Downloads;
using Ludus.Application.Indexers;
using Ludus.Application.Metadata;
using Ludus.Domain.Common;
using Ludus.Domain.Operations;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Metadata;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Operations;

public sealed class HealthService(IDbContextFactory<AppDbContext> factory, IEnumerable<IDownloadClientAdapter> clients,
    IIndexerClient indexers, MetadataService metadata, RemotePathResolver mappings, IRealtimePublisher events)
{
    public async Task<IReadOnlyList<HealthIssue>> CheckAsync(CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var issues = new List<HealthIssue>();
        void Issue(string code, string resource, string message, HealthSeverity severity = HealthSeverity.Warning) => issues.Add(new HealthIssue
            { Code = code, ResourceKey = resource, Message = message, Severity = severity, LastCheckedAtUtc = DateTime.UtcNow });
        foreach (var root in await database.RootFolders.AsNoTracking().ToArrayAsync(cancellationToken))
        {
            try
            {
                if (!Directory.Exists(root.Path)) throw new DirectoryNotFoundException();
                var probe = SafePaths.Within(root.Path, ".ludus-health-" + Guid.NewGuid().ToString("N"));
                await using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ImportException)
            { Issue("RootNotWritable", $"root:{root.Id}", "Root folder is unavailable or not writable.", HealthSeverity.Error); }
        }
        var enabledClients = await database.DownloadClients.AsNoTracking().Where(x => x.Enabled).ToArrayAsync(cancellationToken);
        if (enabledClients.Length == 0) Issue("NoDownloadClient", "downloads", "No enabled download client.");
        foreach (var client in enabledClients)
        {
            try { await clients.Single(x => x.Kind == client.Kind).GetDownloadsAsync(client, cancellationToken); }
            catch (Exception exception) when (exception is DownloadClientException or System.Text.Json.JsonException or InvalidOperationException)
            { Issue("DownloadClientUnavailable", $"client:{client.Id}", "Download client authentication or connection failed.", HealthSeverity.Error); }
        }
        var rss = await database.Indexers.AsNoTracking().Where(x => x.EnableRss).ToArrayAsync(cancellationToken);
        if (rss.Length == 0) Issue("NoRssIndexer", "indexers", "No enabled RSS indexer.");
        foreach (var indexer in rss)
        {
            try { await indexers.GetCapabilitiesAsync(indexer, cancellationToken); }
            catch (IndexerException) { Issue("IndexerUnavailable", $"indexer:{indexer.Id}", "Indexer authentication or connection failed."); }
        }
        foreach (var provider in await database.MetadataProviders.AsNoTracking().Where(x => x.Enabled).ToArrayAsync(cancellationToken))
        {
            try { await metadata.TestAsync(provider.Provider, cancellationToken); }
            catch (MetadataException) { Issue("MetadataUnavailable", $"metadata:{provider.Provider}", "Metadata provider credentials, quota or connection failed."); }
        }
        var configuredProviders = await database.MetadataProviders.AsNoTracking().Where(x => x.Enabled).Select(x => x.Provider).ToArrayAsync(cancellationToken);
        foreach (var platform in await database.Platforms.AsNoTracking().Where(x => x.Monitored && database.Games.Any(g => g.PlatformId == x.Id)).ToArrayAsync(cancellationToken))
        {
            if (!configuredProviders.Contains(platform.PrimaryMetadataProvider)) Issue("MetadataNotConfigured", $"platform:{platform.Id}", "Primary metadata provider is disabled.");
            if (platform.PrimaryMetadataProvider == MetadataProviderKind.Igdb && platform.IgdbPlatformId is null
                || platform.PrimaryMetadataProvider == MetadataProviderKind.ScreenScraper && platform.ScreenScraperSystemId is null)
                Issue("MetadataSystemMappingMissing", $"platform:{platform.Id}", "Platform metadata system mapping is missing.");
        }
        var pathMappings = await database.RemotePathMappings.AsNoTracking().ToArrayAsync(cancellationToken);
        foreach (var download in await database.Downloads.AsNoTracking().Include(x => x.DownloadClient).Where(x => x.RemoteContentPath != null && x.Status != DownloadStatus.Imported && x.Status != DownloadStatus.Failed).ToArrayAsync(cancellationToken))
        {
            try { mappings.Resolve(new Uri(download.DownloadClient.BaseUrl).Host, download.RemoteContentPath!, pathMappings); }
            catch (ImportException) { Issue("RemotePathMappingMissing", $"download:{download.Id}", "Download path cannot be mapped safely.", HealthSeverity.Error); }
        }
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.HealthIssues.ExecuteDeleteAsync(cancellationToken);
        database.HealthIssues.AddRange(issues);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await events.PublishAsync("healthUpdated", new { issues = issues.Select(x => new { x.Code, x.ResourceKey, x.Message, severity = x.Severity.ToString() }) }, cancellationToken);
        return issues;
    }
}
