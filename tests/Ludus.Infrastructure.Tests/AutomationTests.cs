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
using Microsoft.AspNetCore.WebUtilities;
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

    [Fact]
    public async Task DisabledCompletedHandlingKeepsManualImportAvailable()
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
        adapter.Jobs = [new ClientDownload("first", "Example", DownloadStatus.Completed, 9, 9, 0, 0, source, [])];
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var download = await ImportTests.SeedAsync(database, "nes");
        (await database.AppSettings.SingleAsync()).EnableCompletedDownloadHandling = false;
        await database.SaveChangesAsync();
        var coordinator = scope.ServiceProvider.GetRequiredService<DownloadCoordinator>();

        await coordinator.RefreshAsync(default);
        database.ChangeTracker.Clear();
        Assert.Equal(DownloadStatus.Completed, (await database.Downloads.SingleAsync()).Status);
        Assert.Empty(await database.GameFiles.ToArrayAsync());

        await coordinator.ImportCompletedAsync(download.Id, default);
        database.ChangeTracker.Clear();
        Assert.Equal(DownloadStatus.Imported, (await database.Downloads.SingleAsync()).Status);
        Assert.Single(await database.GameFiles.ToArrayAsync());
    }

    [Theory]
    [InlineData(GrabOrigin.Automatic, false, true, false)]
    [InlineData(GrabOrigin.Interactive, true, false, false)]
    [InlineData(GrabOrigin.Automatic, true, false, true)]
    [InlineData(GrabOrigin.Interactive, false, true, true)]
    public async Task ReplacementSearchUsesTheSwitchForThePersistedGrabOrigin(
        GrabOrigin origin,
        bool automaticEnabled,
        bool interactiveEnabled,
        bool replacementExpected)
    {
        var adapter = new FakeAdapter
        {
            Jobs = [new ClientDownload("first", "Example", DownloadStatus.Failed, 9, 0, 0, null, null, [], ClientFailureKind.Temporary, "Failure")]
        };
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
        download.Origin = origin;
        var settings = await database.AppSettings.SingleAsync();
        settings.RedownloadFailed = automaticEnabled;
        settings.RedownloadFailedFromInteractiveSearch = interactiveEnabled;
        var indexer = new Indexer { Name = "Test", BaseUrl = "http://indexer", Protocol = DownloadProtocol.Torrent };
        database.Indexers.Add(indexer);
        await database.SaveChangesAsync();
        var release = new IndexerRelease(indexer.Id, "second", "second", "Example (Europe).nes", 9, DownloadProtocol.Torrent, 10, 10,
            DateTime.UtcNow, [1000], fixture.Secrets.Protect("https://indexer/download"), []);
        search.Result = new InteractiveSearchResult(
            [new EvaluatedRelease(release, new ReleaseParser().Parse(release.Title), new ReleaseDecision(100, [], []))],
            [],
            false);
        var coordinator = scope.ServiceProvider.GetRequiredService<DownloadCoordinator>();

        await coordinator.RefreshAsync(default);
        await coordinator.RefreshAsync(default);
        database.ChangeTracker.Clear();

        Assert.Equal(replacementExpected ? 1 : 0, adapter.Added);
        Assert.Equal(replacementExpected ? 2 : 1, await database.Downloads.CountAsync());
        Assert.Single(await database.ReleaseBlocks.ToArrayAsync());
        Assert.Single(await database.History.Where(x => x.Event == HistoryEvent.DownloadFailed).ToArrayAsync());
        Assert.NotNull((await database.Downloads.SingleAsync(x => x.Id == download.Id)).FailureHandledAtUtc);
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

    [Fact]
    public async Task GotifyTelegramAndGenericWebhooksUseTheirNativePayloads()
    {
        var received = new List<(Uri Uri, JsonElement Body)>();
        using var handler = new DownloadClientTests.Handler(async request =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            received.Add((request.RequestUri!, document.RootElement.Clone()));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        await using var fixture = new TestDatabase(services =>
            services.AddSingleton<IHttpClientFactory>(new DownloadClientTests.Factory(handler)));
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.Webhooks.AddRange(
            new Webhook
            {
                Name = "Gotify", Kind = WebhookKind.Gotify,
                Url = fixture.Secrets.Protect("https://gotify.test/base"),
                Token = fixture.Secrets.Protect("gotify-secret"), Events = [NotificationEvent.Imported]
            },
            new Webhook
            {
                Name = "Telegram", Kind = WebhookKind.Telegram,
                Url = fixture.Secrets.Protect("https://telegram.test"),
                Token = fixture.Secrets.Protect("telegram-secret"), Target = "12345", Events = [NotificationEvent.Imported]
            },
            new Webhook
            {
                Name = "Generic", Kind = WebhookKind.Generic,
                Url = fixture.Secrets.Protect("https://generic.test/hook"), Events = [NotificationEvent.Imported]
            });
        await database.SaveChangesAsync();
        await WebhookService.EnqueueAsync(database, NotificationEvent.Imported, "Example", "Example (Europe)", default);
        await database.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<WebhookService>().DeliverPendingAsync(default);

        var gotify = received.Single(x => x.Uri.Host == "gotify.test");
        Assert.Equal("/base/message", gotify.Uri.AbsolutePath);
        Assert.Equal("gotify-secret", QueryHelpers.ParseQuery(gotify.Uri.Query)["token"]);
        Assert.Equal(5, gotify.Body.GetProperty("priority").GetInt32());
        var telegram = received.Single(x => x.Uri.Host == "telegram.test");
        Assert.Equal("/bottelegram-secret/sendMessage", telegram.Uri.AbsolutePath);
        Assert.Equal("12345", telegram.Body.GetProperty("chat_id").GetString());
        var generic = received.Single(x => x.Uri.Host == "generic.test").Body;
        Assert.Equal("Imported", generic.GetProperty("eventType").GetString());
        Assert.Equal("Example", generic.GetProperty("game").GetProperty("title").GetString());
        Assert.Equal("Example (Europe)", generic.GetProperty("release").GetProperty("title").GetString());
        database.ChangeTracker.Clear();
        Assert.All(await database.WebhookDeliveries.ToArrayAsync(), item => Assert.NotNull(item.CompletedAtUtc));
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
