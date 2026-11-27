using Ludus.Application.Security;
using Ludus.Application.Indexers;
using Ludus.Application.Profiles;
using Ludus.Application.Releases;
using Ludus.Application.Verification;
using Ludus.Infrastructure.Indexers;
using Ludus.Infrastructure.Verification;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Persistence;
using Ludus.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Ludus.Application.Downloads;
using Ludus.Infrastructure.DownloadClients;
using Ludus.Infrastructure.Downloads;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Notifications;
using Ludus.Infrastructure.Workers;
using Ludus.Application.Metadata;
using Ludus.Infrastructure.Metadata;
using Ludus.Infrastructure.Library;
using Ludus.Infrastructure.Operations;

namespace Ludus.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, LudusPaths paths)
    {
        services.AddSingleton(paths);
        services.AddDataProtection().SetApplicationName("Ludus")
            .PersistKeysToFileSystem(new DirectoryInfo(paths.KeysDirectory));
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddDbContextFactory<AppDbContext>(options => DatabaseOptions.Configure(options, paths.DatabasePath), ServiceLifetime.Scoped);
        services.AddSingleton<ReleaseParser>();
        services.AddSingleton<CustomFormatEvaluator>();
        services.AddSingleton<ReleaseScoringEngine>();
        services.AddSingleton<LogiqxDatReader>();
        services.AddSingleton<IFileHasher, FileHasher>();
        services.AddScoped<IDatImporter, DatImporter>();
        services.AddScoped<IDatVerifier, DatVerifier>();
        services.AddSingleton<IndexerXmlParser>();
        services.AddHttpClient<IIndexerClient, TorznabClient>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            }).RemoveAllLoggers();
        services.AddScoped<IInteractiveSearch, InteractiveSearchService>();
        services.AddSingleton<MetadataGate>();
        services.AddScoped<MetadataHttp>();
        services.AddScoped<IMetadataProvider, IgdbProvider>();
        services.AddScoped<IMetadataProvider, ScreenScraperProvider>();
        services.AddScoped<MetadataService>();
        services.AddSingleton<MediaTokens>();
        services.AddScoped<LibraryScanner>();
        services.AddSingleton<BackupGate>();
        services.AddSingleton<ManualTaskQueue>();
        services.AddScoped<BackupService>();
        services.AddScoped<DiagnosticsService>();
        services.AddScoped<HealthService>();
        services.AddHttpClient("DownloadClients", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseCookies = false,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            }).RemoveAllLoggers();
        services.AddScoped<ClientHttp>();
        services.AddScoped<IDownloadClientAdapter, QBittorrentAdapter>();
        services.AddScoped<IDownloadClientAdapter, TransmissionAdapter>();
        services.AddScoped<IDownloadClientAdapter, SabnzbdAdapter>();
        services.AddSingleton<AtomicFiles>();
        services.AddSingleton<ArchiveExtractor>();
        services.AddSingleton<DescriptorFiles>();
        services.AddSingleton<RemotePathResolver>();
        services.AddSingleton<ImportPlanner>();
        services.AddSingleton<ImportGate>();
        services.AddSingleton<DownloadGate>();
        services.AddScoped<ImportService>();
        services.AddScoped<DownloadCoordinator>();
        services.AddScoped<WebhookService>();
        services.AddScoped<IAutomationTask, DownloadRefreshTask>();
        services.AddScoped<IAutomationTask, RssSyncTask>();
        services.AddScoped<IAutomationTask, RecyclingCleanupTask>();
        services.AddScoped<IAutomationTask, LibraryScanTask>();
        services.AddScoped<IAutomationTask, HealthCheckTask>();
        services.AddScoped<IAutomationTask, BackupTask>();
        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<AccountManager>();
        return services;
    }
}
