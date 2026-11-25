using System.Text.Json;
using Ludus.Application.Downloads;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;

namespace Ludus.Infrastructure.DownloadClients;

public sealed class QBittorrentAdapter(ClientHttp http) : IDownloadClientAdapter
{
    public DownloadClientKind Kind => DownloadClientKind.QBittorrent;

    public async Task<IReadOnlyList<ClientDownload>> GetDownloadsAsync(DownloadClient client, CancellationToken cancellationToken)
    {
        var cookie = await LoginAsync(client, cancellationToken);
        using var response = await RequestAsync(client, cookie, "torrents/info?category=ludus", null, cancellationToken);
        var json = await ClientHttp.JsonAsync(response, cancellationToken);
        if (json.ValueKind != JsonValueKind.Array) throw new DownloadClientException("InvalidResponse");
        var snapshots = json.EnumerateArray().Where(x => ClientHttp.Text(x, "category") == "ludus").Select(x =>
        {
            var state = ClientHttp.Text(x, "state");
            var complete = ClientHttp.Decimal(x, "progress") >= 1 && state is not ("checkingDL" or "checkingUP" or "moving" or "allocating");
            var failure = state switch { "missingFiles" or "error" => ClientFailureKind.LocalConfiguration, _ => ClientFailureKind.None };
            var status = failure != ClientFailureKind.None ? DownloadStatus.Failed : complete ? DownloadStatus.Completed
                : state.StartsWith("paused", StringComparison.Ordinal) || state.StartsWith("stopped", StringComparison.Ordinal) ? DownloadStatus.Paused : DownloadStatus.Downloading;
            return new ClientDownload(ClientHttp.Text(x, "hash"), ClientHttp.Text(x, "name"), status,
                Math.Max(0, ClientHttp.Number(x, "size")), Math.Max(0, ClientHttp.Number(x, "downloaded")), Math.Max(0, ClientHttp.Number(x, "dlspeed")),
                ClientHttp.Eta(ClientHttp.Number(x, "eta")), ClientHttp.Text(x, "content_path"), ClientHttp.Tokens(ClientHttp.Text(x, "tags")),
                failure, failure == ClientFailureKind.None ? null : "ClientStorageError", complete && (state is "pausedUP" or "stoppedUP") && SeedGoalMet(x));
        }).ToArray();
        foreach (var stalled in json.EnumerateArray().Where(x => ClientHttp.Text(x, "category") == "ludus" && ClientHttp.Text(x, "state") == "stalledDL"))
        {
            var id = ClientHttp.Text(stalled, "hash");
            using var trackersResponse = await RequestAsync(client, cookie, "torrents/trackers?hash=" + Uri.EscapeDataString(id), null, cancellationToken);
            var trackers = (await ClientHttp.JsonAsync(trackersResponse, cancellationToken)).EnumerateArray()
                .Where(x => ClientHttp.Text(x, "url").StartsWith("http", StringComparison.OrdinalIgnoreCase) || ClientHttp.Text(x, "url").StartsWith("udp:", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (trackers.Length > 0 && trackers.All(x => ClientHttp.Number(x, "status") == 4 && NetworkError(ClientHttp.Text(x, "msg"))))
            {
                var position = Array.FindIndex(snapshots, x => x.Id == id);
                snapshots[position] = snapshots[position] with { Status = DownloadStatus.Failed, FailureKind = ClientFailureKind.Temporary, FailureCode = "TrackerNetworkFailure" };
            }
        }
        return snapshots;
    }

    public async Task<string?> AddAsync(DownloadClient client, string sourceUrl, string title, string submissionToken, CancellationToken cancellationToken)
    {
        ClientHttp.ValidateSource(sourceUrl, true);
        var cookie = await LoginAsync(client, cancellationToken);
        using var response = await RequestAsync(client, cookie, "torrents/add", new Dictionary<string, string>
        {
            ["urls"] = sourceUrl, ["category"] = "ludus", ["tags"] = submissionToken
        }, cancellationToken);
        ClientHttp.RequireSuccess(response);
        if (!(await response.Content.ReadAsStringAsync(cancellationToken)).Trim().Equals("Ok.", StringComparison.OrdinalIgnoreCase))
            throw new DownloadClientException("AddRejected");
        return (await GetDownloadsAsync(client, cancellationToken)).SingleOrDefault(x => x.Labels.Contains(submissionToken))?.Id;
    }

    public async Task RemoveAsync(DownloadClient client, string id, bool deleteData, CancellationToken cancellationToken)
    {
        var download = (await GetDownloadsAsync(client, cancellationToken)).SingleOrDefault(x => x.Id == id);
        if (download is null) return;
        var cookie = await LoginAsync(client, cancellationToken);
        using var response = await RequestAsync(client, cookie, "torrents/delete", new Dictionary<string, string>
        {
            ["hashes"] = id, ["deleteFiles"] = deleteData ? "true" : "false"
        }, cancellationToken);
        ClientHttp.RequireSuccess(response);
    }

    public async Task MarkImportedAsync(DownloadClient client, string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(client.PostImportTag)) return;
        if (!(await GetDownloadsAsync(client, cancellationToken)).Any(x => x.Id == id)) throw new DownloadClientException("CategoryOwnershipLost");
        var cookie = await LoginAsync(client, cancellationToken);
        using var response = await RequestAsync(client, cookie, "torrents/addTags", new Dictionary<string, string>
        {
            ["hashes"] = id, ["tags"] = client.PostImportTag
        }, cancellationToken);
        ClientHttp.RequireSuccess(response);
    }

    private async Task<string> LoginAsync(DownloadClient client, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ClientHttp.Endpoint(client, "/api/v2/"), "auth/login"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = http.Secret(client.Username), ["password"] = http.Secret(client.Password) })
        };
        request.Headers.Referrer = new Uri(client.BaseUrl);
        using var response = await http.SendAsync(request, false, cancellationToken);
        ClientHttp.RequireSuccess(response);
        if (!(await response.Content.ReadAsStringAsync(cancellationToken)).Trim().Equals("Ok.", StringComparison.OrdinalIgnoreCase))
            throw new DownloadClientException("AuthenticationFailed");
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.Select(x => x.Split(';')[0]) : [];
        return string.Join("; ", cookies.Where(x => x.StartsWith("SID=", StringComparison.Ordinal) || x.StartsWith("QBT_SID_", StringComparison.Ordinal)));
    }

    private static bool SeedGoalMet(JsonElement torrent) =>
        torrent.TryGetProperty("max_ratio", out _) && ClientHttp.Decimal(torrent, "max_ratio") >= 0 && ClientHttp.Decimal(torrent, "ratio") >= ClientHttp.Decimal(torrent, "max_ratio")
        || torrent.TryGetProperty("max_seeding_time", out _) && ClientHttp.Number(torrent, "max_seeding_time") >= 0 && ClientHttp.Number(torrent, "seeding_time") >= ClientHttp.Number(torrent, "max_seeding_time") * 60;

    private static bool NetworkError(string message) => new[] { "timed out", "timeout", "resolve", "unreachable", "connection refused" }
        .Any(word => message.Contains(word, StringComparison.OrdinalIgnoreCase));

    private Task<HttpResponseMessage> RequestAsync(DownloadClient client, string cookie, string method, Dictionary<string, string>? form, CancellationToken cancellationToken)
        => SendAsync(client, cookie, method, form, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(DownloadClient client, string cookie, string method, Dictionary<string, string>? form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(form is null ? HttpMethod.Get : HttpMethod.Post, new Uri(ClientHttp.Endpoint(client, "/api/v2/"), method));
        if (form is not null) request.Content = new FormUrlEncodedContent(form);
        if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
        request.Headers.Referrer = new Uri(client.BaseUrl);
        return await http.SendAsync(request, form is not null, cancellationToken);
    }
}
