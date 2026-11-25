using System.Net;
using System.Text.Json;
using Ludus.Application.Downloads;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;
using Ludus.Infrastructure.DownloadClients;
using Microsoft.AspNetCore.WebUtilities;

namespace Ludus.Infrastructure.Tests;

public sealed class DownloadClientTests
{
    [Fact]
    public async Task QBittorrentFiltersCategoryAndCannotDeleteForeignTorrent()
    {
        await using var fixture = new TestDatabase();
        var mutations = new List<string>();
        using var handler = new Handler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("auth/login"))
            {
                var response = Text("Ok.");
                response.Headers.Add("Set-Cookie", "QBT_SID_8080=session; HttpOnly");
                return response;
            }
            Assert.Contains("QBT_SID_8080=session", request.Headers.GetValues("Cookie"));
            if (path.EndsWith("torrents/info"))
            {
                Assert.Equal("ludus", QueryHelpers.ParseQuery(request.RequestUri.Query)["category"]);
                return Json("""
                    [{"hash":"owned","name":"Game","category":"ludus","progress":1,"state":"uploading","content_path":"/downloads/game.nes","size":9,"tags":"ludus-token"},
                     {"hash":"foreign","name":"Other","category":"radarr","progress":1,"state":"pausedUP"}]
                    """);
            }
            mutations.Add(await request.Content!.ReadAsStringAsync());
            return Text("");
        });
        var adapter = new QBittorrentAdapter(new ClientHttp(new Factory(handler), fixture.Secrets));
        var client = Client(DownloadClientKind.QBittorrent);
        var jobs = await adapter.GetDownloadsAsync(client, default);
        Assert.Equal("/downloads/game.nes", Assert.Single(jobs).ContentPath);
        Assert.False(jobs[0].CanRemove);
        await adapter.RemoveAsync(client, "foreign", true, default);
        Assert.Empty(mutations);
        await adapter.MarkImportedAsync(client, "owned", default);
        Assert.Contains("ludus-imported", Assert.Single(mutations));
    }

    [Fact]
    public async Task QBittorrentSubmissionCarriesCategoryAndUniqueReconciliationTag()
    {
        await using var fixture = new TestDatabase();
        using var handler = new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("auth/login")) return Text("Ok.");
            if (request.RequestUri.AbsolutePath.EndsWith("torrents/add"))
            {
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
                Assert.Equal("ludus", form["category"]);
                Assert.Equal("ludus-token", form["tags"]);
                return Text("Ok.");
            }
            return Json("[{\"hash\":\"abc\",\"category\":\"ludus\",\"tags\":\"ludus-token\"}]");
        });
        var adapter = new QBittorrentAdapter(new ClientHttp(new Factory(handler), fixture.Secrets));
        Assert.Equal("abc", await adapter.AddAsync(Client(DownloadClientKind.QBittorrent), "magnet:?xt=urn:btih:abc", "Game", "ludus-token", default));
    }

    [Fact]
    public async Task TransmissionNegotiatesSessionAndOnlyRequestsDetailsForOwnedLabels()
    {
        await using var fixture = new TestDatabase();
        var handshakes = 0;
        using var handler = new Handler(async request =>
        {
            if (!request.Headers.Contains("X-Transmission-Session-Id"))
            {
                handshakes++;
                var conflict = new HttpResponseMessage(HttpStatusCode.Conflict);
                conflict.Headers.Add("X-Transmission-Session-Id", "session");
                return conflict;
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var arguments = body.RootElement.GetProperty("arguments");
            if (!arguments.TryGetProperty("ids", out var ids)) return Json("""
                {"result":"success","arguments":{"torrents":[{"hashString":"mine","labels":["ludus"]},{"hashString":"theirs","labels":["radarr"]}]}}
                """);
            Assert.Equal("mine", Assert.Single(ids.EnumerateArray()).GetString());
            return Json("""
                {"result":"success","arguments":{"torrents":[{"hashString":"mine","name":"game","labels":["ludus"],"status":6,
                "sizeWhenDone":9,"leftUntilDone":0,"metadataPercentComplete":1,"downloadDir":"/downloads","files":[{"name":"game/game.nes"}],"isFinished":false}]}}
                """);
        });
        var adapter = new TransmissionAdapter(new ClientHttp(new Factory(handler), fixture.Secrets));
        var result = Assert.Single(await adapter.GetDownloadsAsync(Client(DownloadClientKind.Transmission), default));
        Assert.Equal(2, handshakes);
        Assert.Equal("/downloads/game/game.nes", result.ContentPath);
        Assert.Equal(DownloadStatus.Completed, result.Status);
        Assert.False(result.CanRemove);
    }

    [Fact]
    public async Task SabnzbdFiltersBothQueueAndHistoryAndDistinguishesBrokenArchives()
    {
        await using var fixture = new TestDatabase();
        using var handler = new Handler(async request =>
        {
            Assert.Equal("", request.RequestUri!.Query);
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
            Assert.Equal("ludus", form["cat"]);
            Assert.Equal("private-key", form["apikey"]);
            return form["mode"] == "queue" ? Json("{\"queue\":{\"slots\":[{\"cat\":\"radarr\",\"nzo_id\":\"foreign\"}]}}") : Json("""
                {"history":{"slots":[{"cat":"ludus","nzo_id":"mine","name":"game","status":"Failed","fail_message":"Unpack failed: CRC error","storage":"/downloads/game","bytes":9}]}}
                """);
        });
        var client = Client(DownloadClientKind.Sabnzbd);
        client.ApiKey = fixture.Secrets.Protect("private-key");
        var adapter = new SabnzbdAdapter(new ClientHttp(new Factory(handler), fixture.Secrets));
        var result = Assert.Single(await adapter.GetDownloadsAsync(client, default));
        Assert.Equal("mine", result.Id);
        Assert.Equal(ClientFailureKind.InvalidContent, result.FailureKind);
        Assert.Equal("/downloads/game", result.ContentPath);
    }

    private static DownloadClient Client(DownloadClientKind kind) => new() { Name = "Test", BaseUrl = "http://localhost:1234", Kind = kind };
    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static HttpResponseMessage Json(string value) => Text(value);
    internal sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    internal sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
