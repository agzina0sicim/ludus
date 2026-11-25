using System.Net;
using System.Text.Json;
using Ludus.Application.Downloads;
using Ludus.Application.Indexers;
using Ludus.Application.Profiles;
using Ludus.Application.Releases;
using Ludus.Domain.Activity;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;
using Ludus.Infrastructure.Downloads;
using Ludus.Infrastructure.Notifications;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ludus.Infrastructure.Tests;

public sealed class AutomationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedDownloadsImportAndOnlyRemoveAfterSeedingGoal(bool seedingFinished)
    {
        var adapter = new FakeAdapter();
        await using var fixture = new TestDatabase(services =>
        {
            services.RemoveAll<IDownloadClientAdapter>();
            services.AddSingleton<IDownloadClientAdapter>(adapter);
        });
        await fixture.InitializeAsync();
        Directory.CreateDirectory(fixture.Paths.DownloadDirectory);
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        var source = Path.Combine(fixture.Paths.DownloadDirectory, "Example (USA).nes");
        await File.WriteAllTextAsync(source, "game data");
        adapter.Jobs = [new ClientDownload("first", "Example", DownloadStatus.Completed, 9, 9, 0, 0, source, [], CanRemove: seedingFinished)];
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var download = await ImportTests.SeedAsync(database, "nes");
        download.DownloadClient.RemoveCompleted = true;
        await database.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<DownloadCoordinator>().RefreshAsync(default);
        database.ChangeTracker.Clear();
        var imported = await database.Downloads.SingleAsync();
        Assert.Equal(DownloadStatus.Imported, imported.Status);
        Assert.Equal(!seedingFinished, imported.CleanupPending);
        Assert.Equal(GameStatus.Available, (await database.Games.SingleAsync()).Status);
        Assert.Single(await database.GameFiles.ToArrayAsync());
        Assert.Single(adapter.Tagged);
        Assert.True(File.Exists(source));
        if (seedingFinished) Assert.Equal(("first", false), Assert.Single(adapter.Removed));
        else Assert.Empty(adapter.Removed);
    }

    [Theory]
    [InlineData(ClientFailureKind.Temporary, BlockKind.Cooldown, false)]
    [InlineData(ClientFailureKind.InvalidContent, BlockKind.Permanent, true)]
    public async Task FailedDownloadsAreBlockedCleanedAndReplaced(ClientFailureKind failure, BlockKind expected, bool deleteData)
    {
        var adapter = new FakeAdapter { Jobs = [new ClientDownload("first", "Example", DownloadStatus.Failed, 9, 0, 0, null, null, [], failure, "Failure")] };
        var search = new FakeSearch();
        await using var fixture = new TestDatabase(services =>
        {
            services.RemoveAll<IDownloadClientAdapter>();
            services.AddSingleton<IDownloadClientAdapter>(adapter);
            services.RemoveAll<IInteractiveSearch>();
            services.AddSingleton<IInteractiveSearch>(search);
        });
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var download = await ImportTests.SeedAsync(database, "nes");
        var indexer = new Indexer { Name = "Test", BaseUrl = "http://indexer", Protocol = DownloadProtocol.Torrent };
        database.Indexers.Add(indexer);
        await database.SaveChangesAsync();
        var release = new IndexerRelease(indexer.Id, "second", "second", "Example (Europe).nes", 9, DownloadProtocol.Torrent, 10, 10,
            DateTime.UtcNow, [1000], fixture.Secrets.Protect("https://indexer/download"), []);
        search.Result = new InteractiveSearchResult([new EvaluatedRelease(release, new ReleaseParser().Parse(release.Title), new ReleaseDecision(100, [], []))], [], false);
        var started = DateTime.UtcNow;
        await scope.ServiceProvider.GetRequiredService<DownloadCoordinator>().RefreshAsync(default);
        database.ChangeTracker.Clear();
        var block = await database.ReleaseBlocks.SingleAsync();
        Assert.Equal(expected, block.Kind);
        if (expected == BlockKind.Cooldown) Assert.InRange(block.ExpiresAtUtc!.Value, started.AddDays(7), DateTime.UtcNow.AddDays(7));
        else Assert.Null(block.ExpiresAtUtc);
        Assert.Equal(("first", deleteData), Assert.Single(adapter.Removed));
        Assert.Equal(1, adapter.Added);
        Assert.True(search.Automatic);
        Assert.Equal(2, await database.Downloads.CountAsync());
        Assert.Equal(DownloadStatus.Failed, (await database.Downloads.SingleAsync(x => x.Id == download.Id)).Status);
        Assert.Equal("replacement", (await database.Downloads.SingleAsync(x => x.Id != download.Id)).DownloadId);
        Assert.Contains(await database.History.ToArrayAsync(), x => x.Event == HistoryEvent.DownloadFailed);
        Assert.Contains(await database.History.ToArrayAsync(), x => x.Event == HistoryEvent.Grabbed);
    }

    [Fact]
    public async Task LocalStorageFailuresDoNotPoisonTheBlocklist()
    {
        var adapter = new FakeAdapter { Jobs = [new ClientDownload("first", "Example", DownloadStatus.Failed, 9, 0, 0, null, null, [], ClientFailureKind.LocalConfiguration, "DiskFull")] };
        await using var fixture = new TestDatabase(services => { services.RemoveAll<IDownloadClientAdapter>(); services.AddSingleton<IDownloadClientAdapter>(adapter); });
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ImportTests.SeedAsync(database, "nes");
        await scope.ServiceProvider.GetRequiredService<DownloadCoordinator>().RefreshAsync(default);
        Assert.Empty(await database.ReleaseBlocks.ToArrayAsync());
        Assert.Empty(adapter.Removed);
        Assert.Equal(0, adapter.Added);
    }

    [Fact]
    public async Task WebhooksPersistRetryStateAndSuppressMentions()
    {
        var attempts = 0;
        using var handler = new DownloadClientTests.Handler(async request =>
        {
            Assert.Contains("private-token", request.RequestUri!.AbsoluteUri);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(0, document.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength());
            attempts++;
            return new HttpResponseMessage(attempts == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.NoContent);
        });
        await using var fixture = new TestDatabase(services => services.AddSingleton<IHttpClientFactory>(new DownloadClientTests.Factory(handler)));
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.Webhooks.Add(new Webhook { Name = "Discord", Url = fixture.Secrets.Protect("https://webhook.test/private-token") });
        await database.SaveChangesAsync();
        await WebhookService.EnqueueAsync(database, NotificationEvent.Imported, "@everyone", "Example", default);
        await database.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<WebhookService>();
        await service.DeliverPendingAsync(default);
        database.ChangeTracker.Clear();
        var failed = await database.WebhookDeliveries.SingleAsync();
        Assert.Equal(1, failed.Attempts);
        Assert.Null(failed.CompletedAtUtc);
        Assert.Equal("Http429", failed.ErrorCode);
        failed.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await database.SaveChangesAsync();
        await service.DeliverPendingAsync(default);
        database.ChangeTracker.Clear();
        Assert.NotNull((await database.WebhookDeliveries.SingleAsync()).CompletedAtUtc);
        Assert.Equal(2, attempts);
    }

    private sealed class FakeAdapter : IDownloadClientAdapter
    {
        public DownloadClientKind Kind => DownloadClientKind.QBittorrent;
        public IReadOnlyList<ClientDownload> Jobs { get; set; } = [];
        public List<(string Id, bool DeleteData)> Removed { get; } = [];
        public List<string> Tagged { get; } = [];
        public int Added { get; private set; }
        public Task<IReadOnlyList<ClientDownload>> GetDownloadsAsync(DownloadClient client, CancellationToken cancellationToken) => Task.FromResult(Jobs);
        public Task<string?> AddAsync(DownloadClient client, string sourceUrl, string title, string submissionToken, CancellationToken cancellationToken)
        {
            Added++;
            Assert.StartsWith("ludus-", submissionToken);
            return Task.FromResult<string?>("replacement");
        }
        public Task RemoveAsync(DownloadClient client, string id, bool deleteData, CancellationToken cancellationToken) { Removed.Add((id, deleteData)); return Task.CompletedTask; }
        public Task MarkImportedAsync(DownloadClient client, string id, CancellationToken cancellationToken) { Tagged.Add(id); return Task.CompletedTask; }
    }

    private sealed class FakeSearch : IInteractiveSearch
    {
        public bool Automatic { get; private set; }
        public InteractiveSearchResult Result { get; set; } = new([], [], false);
        public Task<InteractiveSearchResult> SearchAsync(long gameId, CancellationToken cancellationToken = default, bool automatic = false)
        {
            Automatic = automatic;
            return Task.FromResult(Result);
        }
    }
}
