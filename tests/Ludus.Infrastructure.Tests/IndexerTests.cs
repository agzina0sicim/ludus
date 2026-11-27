using System.Net;
using System.Text.Json;
using Ludus.Application.Indexers;
using Ludus.Application.Profiles;
using Ludus.Application.Releases;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Indexers;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ludus.Infrastructure.Tests;

public sealed class IndexerTests
{
    [Fact]
    public void ReleasePolicyAppliesAgeOnlyToDatedUsenetAndMaximumSizeToEveryProtocol()
    {
        var now = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        var settings = new Ludus.Domain.Configuration.AppSettings
        {
            RecyclingBinPath = "recycling",
            MinimumAgeMinutes = 60,
            MaximumIndexerSizeBytes = 1_000
        };
        IndexerRelease Release(DownloadProtocol protocol, long size, DateTime? published) =>
            new(1, "id", "fingerprint", "Example", size, protocol, 1, 1, published, [1000], null, []);

        var rejected = ReleasePolicy.Rejections(Release(DownloadProtocol.Usenet, 1_001, now.AddMinutes(-30)), settings, now);
        Assert.Contains(rejected, x => x.Code == RejectionCode.TooYoung);
        Assert.Contains(rejected, x => x.Code == RejectionCode.TooLarge);
        Assert.Empty(ReleasePolicy.Rejections(Release(DownloadProtocol.Usenet, 1_000, null), settings, now));
        Assert.Empty(ReleasePolicy.Rejections(Release(DownloadProtocol.Torrent, 1_000, now), settings, now));
    }

    private const string Caps = """
        <caps><limits max="2" default="1"/><searching><search available="yes" supportedParams="q"/></searching>
        <categories><category id="1000" name="Console"><subcat id="1010" name="NDS"/></category></categories></caps>
        """;

    [Theory]
    [InlineData("http://localhost:9696/1/api", "/1/api")]
    [InlineData("http://localhost:9117/api/v2.0/indexers/test/results/torznab", "/api/v2.0/indexers/test/results/torznab/api")]
    public async Task EncodesQueriesAndSupportsProwlarrAndJackettPaths(string baseUrl, string path)
    {
        await using var fixture = new TestDatabase();
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal(path, request.RequestUri!.AbsolutePath);
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            Assert.Equal("search", query["t"]);
            Assert.Equal("A & B + C", query["q"]);
            Assert.Equal("a&b+c", query["apikey"]);
            Assert.Equal("1000,1010", query["cat"]);
            Assert.Equal("10", query["offset"]);
            return Task.FromResult(Xml(Feed("Example (Europe).nes")));
        });
        using var http = new HttpClient(handler);
        var client = Client(http, fixture);
        var indexer = new Indexer { Id = 5, Name = "Test", BaseUrl = baseUrl, ApiKey = fixture.Secrets.Protect("a&b+c") };
        var page = await client.SearchAsync(indexer, new IndexerQuery("A & B + C", [1000, 1010], 10, 2));
        Assert.Single(page.Releases);
    }

    [Fact]
    public async Task RssFetchesCapabilitiesAndUsesSearchWithoutQueryAtSupportedLimit()
    {
        await using var fixture = new TestDatabase();
        var requests = new List<string>();
        using var handler = new FakeHandler((request, _) =>
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            requests.Add(query["t"].ToString());
            if (query["t"] == "caps") return Task.FromResult(Xml(Caps));
            Assert.False(query.ContainsKey("q"));
            Assert.Equal("2", query["limit"]);
            return Task.FromResult(Xml(Feed("Example (Europe).nes")));
        });
        using var http = new HttpClient(handler);
        var result = await Client(http, fixture).GetRssAsync(Indexer());
        Assert.Single(result.Releases);
        Assert.Equal(["caps", "search"], requests);
    }

    [Theory]
    [InlineData("http://torznab.com/schemas/2015/feed", DownloadProtocol.Torrent)]
    [InlineData("http://www.newznab.com/DTD/2010/feeds/attributes/", DownloadProtocol.Usenet)]
    public async Task ParsesBothNamespacesAndNeverSerializesCredentialBearingUrls(string ns, DownloadProtocol protocol)
    {
        await using var fixture = new TestDatabase();
        using var handler = new FakeHandler((_, _) => Task.FromResult(Xml(Feed("Example (Europe).nes", ns: ns))));
        using var http = new HttpClient(handler);
        var indexer = Indexer();
        indexer.Protocol = protocol;
        var release = Assert.Single((await Client(http, fixture).SearchAsync(indexer, new IndexerQuery())).Releases);
        Assert.Equal(123456, release.Size);
        Assert.Equal(12, release.Seeders);
        Assert.Equal([1000, 1010], release.Categories);
        Assert.Equal(DateTimeKind.Utc, release.PublishedAtUtc!.Value.Kind);
        Assert.Equal(protocol, release.Protocol);
        Assert.Contains("secret-download-key", fixture.Secrets.Unprotect(release.DownloadUrl!));
        Assert.DoesNotContain("secret-download-key", JsonSerializer.Serialize(release));
        Assert.DoesNotContain("secret-guid-key", JsonSerializer.Serialize(release));
        Assert.Equal(64, release.Fingerprint.Length);
    }

    [Fact]
    public async Task ParsesMagnetFallbackAndReportsMalformedItemsWithoutDroppingThem()
    {
        await using var fixture = new TestDatabase();
        const string xml = """
            <rss xmlns:t="http://torznab.com/schemas/2015/feed"><channel>
            <item><title>Example</title><guid>one</guid><t:attr name="magneturl" value="magnet:?xt=urn:btih:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"/></item>
            <item><guid>two</guid><enclosure url="file:///not-allowed"/></item>
            </channel></rss>
            """;
        using var handler = new FakeHandler((_, _) => Task.FromResult(Xml(xml)));
        using var http = new HttpClient(handler);
        var results = (await Client(http, fixture).SearchAsync(Indexer(), new IndexerQuery())).Releases;
        Assert.Equal(2, results.Count);
        Assert.StartsWith("magnet:", fixture.Secrets.Unprotect(results[0].DownloadUrl!));
        Assert.Null(results[1].DownloadUrl);
        Assert.Contains("InvalidTitle", results[1].ParseWarnings);
        Assert.Contains("InvalidSize", results[1].ParseWarnings);
    }

    [Theory]
    [InlineData("<error code='100' description='secret-value'/>", "ApiError100")]
    [InlineData("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///secret'>]><rss><channel/></rss>", "InvalidXml")]
    [InlineData("<html>not xml feed</html>", "InvalidFeed")]
    [InlineData("<rss>", "InvalidXml")]
    public async Task ReturnsSafeErrorsForApiFailuresAndUnsafeXml(string response, string code)
    {
        await using var fixture = new TestDatabase();
        using var handler = new FakeHandler((_, _) => Task.FromResult(Xml(response)));
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<IndexerException>(() => Client(http, fixture).SearchAsync(Indexer(), new IndexerQuery()));
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public async Task ReportsRateLimitAndSanitizesConnectionExceptions()
    {
        await using var fixture = new TestDatabase();
        using var handler = new FakeHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<IndexerException>(() => Client(http, fixture).GetCapabilitiesAsync(Indexer()));
        Assert.Equal("Http429", error.Code);
        Assert.Equal(60, error.RetryAfterSeconds);
        using var failingHandler = new FakeHandler((_, _) => throw new HttpRequestException("https://indexer/?apikey=secret"));
        using var failingHttp = new HttpClient(failingHandler);
        var connectionError = await Assert.ThrowsAsync<IndexerException>(() => Client(failingHttp, fixture).GetCapabilitiesAsync(Indexer()));
        Assert.DoesNotContain("apikey", connectionError.ToString());
        Assert.Null(connectionError.InnerException);
    }

    [Fact]
    public async Task RejectsRedirectsAndOversizedResponsesBeforeParsing()
    {
        await using var fixture = new TestDatabase();
        using var redirect = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("http://different.test/?apikey=secret") }
        }));
        using var redirectHttp = new HttpClient(redirect);
        Assert.Equal("Http302", (await Assert.ThrowsAsync<IndexerException>(() => Client(redirectHttp, fixture).GetCapabilitiesAsync(Indexer()))).Code);
        using var oversized = new FakeHandler((_, _) =>
        {
            var response = Xml(Caps);
            response.Content.Headers.ContentLength = 17 * 1024 * 1024;
            return Task.FromResult(response);
        });
        using var largeHttp = new HttpClient(oversized);
        Assert.Equal("ResponseTooLarge", (await Assert.ThrowsAsync<IndexerException>(() => Client(largeHttp, fixture).GetCapabilitiesAsync(Indexer()))).Code);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedToIndexerFailure()
    {
        await using var fixture = new TestDatabase();
        using var cancellation = new CancellationTokenSource();
        using var handler = new FakeHandler(async (_, token) =>
        {
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
            return Xml(Caps);
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(http, fixture).GetCapabilitiesAsync(Indexer(), cancellation.Token));
    }

    [Fact]
    public async Task InteractiveSearchPaginatesKeepsRejectionsAndIsolatesIndexerFailure()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = new Game
        {
            Title = "Example", SortTitle = "Example", PlatformId = (await database.Platforms.SingleAsync(x => x.Slug == "nes")).Id,
            RootFolderId = (await database.RootFolders.SingleAsync()).Id, QualityProfileId = (await database.QualityProfiles.SingleAsync()).Id
        };
        database.Games.Add(game);
        database.Indexers.AddRange(Indexer("http://good.test"), Indexer("http://broken.test"));
        await database.SaveChangesAsync();
        var offsets = new List<int>();
        using var handler = new FakeHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "broken.test") return Task.FromResult(Xml("<error code='100' description='bad key'/>"));
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            if (query["t"] == "caps") return Task.FromResult(Xml(Caps.Replace("max=\"2\"", "max=\"1\"")));
            var offset = int.Parse(query["offset"].ToString());
            offsets.Add(offset);
            return Task.FromResult(Xml(Feed(offset == 0 ? "Example (Europe).nes" : "Example (Japan) (Beta).nsp", offset, 2)));
        });
        using var http = new HttpClient(handler);
        var service = new InteractiveSearchService(scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            Client(http, fixture), new ReleaseParser(), new ReleaseScoringEngine(new CustomFormatEvaluator()), NullLogger<InteractiveSearchService>.Instance);
        var result = await service.SearchAsync(game.Id);
        Assert.Equal([0, 1], offsets);
        Assert.Equal(2, result.Releases.Count);
        Assert.True(result.Releases[0].Decision.Accepted);
        Assert.False(result.Releases[1].Decision.Accepted);
        Assert.Contains(result.Releases[1].Decision.Rejections, x => x.Code == RejectionCode.BetaRejected);
        Assert.Contains(result.Releases[1].Decision.Rejections, x => x.Code == RejectionCode.FormatNotAllowed);
        Assert.Equal("ApiError100", Assert.Single(result.Failures).Code);
        Assert.False(result.Truncated);
        database.ChangeTracker.Clear();
        Assert.NotNull((await database.Indexers.SingleAsync(x => x.BaseUrl == "http://good.test")).CapabilitiesUpdatedAtUtc);
    }

    [Fact]
    public async Task RepeatingPagesAreReportedAsTruncatedWithoutDuplicatingRows()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = new Game
        {
            Title = "Example", SortTitle = "Example", PlatformId = (await database.Platforms.FirstAsync()).Id,
            RootFolderId = (await database.RootFolders.SingleAsync()).Id, QualityProfileId = (await database.QualityProfiles.SingleAsync()).Id
        };
        database.Games.Add(game);
        database.Indexers.Add(Indexer());
        await database.SaveChangesAsync();
        using var handler = new FakeHandler((request, _) => Task.FromResult(Xml(
            QueryHelpers.ParseQuery(request.RequestUri!.Query)["t"] == "caps"
                ? Caps.Replace("max=\"2\"", "max=\"1\"") : Feed("Example (Europe).nes", total: 5))));
        using var http = new HttpClient(handler);
        var service = new InteractiveSearchService(scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            Client(http, fixture), new ReleaseParser(), new ReleaseScoringEngine(new CustomFormatEvaluator()), NullLogger<InteractiveSearchService>.Instance);
        var result = await service.SearchAsync(game.Id);
        Assert.True(result.Truncated);
        Assert.Single(result.Releases);
        Assert.Equal("PaginationStalled", Assert.Single(result.Failures).Code);
    }

    private static Indexer Indexer(string url = "http://localhost:9696/1/api") => new() { Name = url, BaseUrl = url, Protocol = DownloadProtocol.Torrent };
    private static TorznabClient Client(HttpClient http, TestDatabase fixture) => new(http, fixture.Secrets, new IndexerXmlParser(fixture.Secrets));
    private static HttpResponseMessage Xml(string xml) => new(HttpStatusCode.OK) { Content = new StringContent(xml, System.Text.Encoding.UTF8, "application/xml") };
    private static string Feed(string title, int offset = 0, int total = 1, string ns = "http://torznab.com/schemas/2015/feed") => $"""
        <rss xmlns:t="{ns}"><channel><t:response offset="{offset}" total="{total}"/>
        <item><title>{title}</title><guid>http://tracker/?id={offset}&amp;apikey=secret-guid-key</guid>
        <pubDate>Fri, 20 Nov 2026 12:00:00 +0100</pubDate>
        <enclosure url="http://tracker/get?id={offset}&amp;apikey=secret-download-key" length="123456" type="application/x-bittorrent"/>
        <t:attr name="size" value="123456"/><t:attr name="seeders" value="12"/>
        <t:attr name="category" value="1000"/><t:attr name="category" value="1010"/><t:attr name="category" value="1010"/>
        </item></channel></rss>
        """;

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
