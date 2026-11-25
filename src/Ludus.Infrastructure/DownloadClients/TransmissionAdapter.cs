using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ludus.Application.Downloads;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;

namespace Ludus.Infrastructure.DownloadClients;

public sealed class TransmissionAdapter(ClientHttp http) : IDownloadClientAdapter
{
    public DownloadClientKind Kind => DownloadClientKind.Transmission;

    public async Task<IReadOnlyList<ClientDownload>> GetDownloadsAsync(DownloadClient client, CancellationToken cancellationToken)
    {
        var inventory = await RpcAsync(client, "torrent-get", new { fields = new[] { "hashString", "labels" } }, false, cancellationToken);
        var ids = inventory.GetProperty("torrents").EnumerateArray().Where(x => ClientHttp.Labels(x, "labels").Contains("ludus"))
            .Select(x => ClientHttp.Text(x, "hashString")).ToArray();
        if (ids.Length == 0) return [];
        var details = await RpcAsync(client, "torrent-get", new
        {
            ids, fields = new[] { "hashString", "name", "labels", "status", "totalSize", "sizeWhenDone", "leftUntilDone", "rateDownload", "eta",
                "downloadDir", "files", "error", "errorString", "isFinished", "metadataPercentComplete" }
        }, false, cancellationToken);
        return details.GetProperty("torrents").EnumerateArray().Where(x => ClientHttp.Labels(x, "labels").Contains("ludus"))
            .Select(x =>
            {
                var size = Math.Max(0, ClientHttp.Number(x, "sizeWhenDone"));
                var remaining = Math.Max(0, ClientHttp.Number(x, "leftUntilDone"));
                var complete = size > 0 && remaining == 0 && ClientHttp.Decimal(x, "metadataPercentComplete") >= 1 && ClientHttp.Number(x, "status") is not (1 or 2);
                var failure = ClientHttp.Number(x, "error") switch { 1 or 2 when !complete => ClientFailureKind.Temporary, 3 => ClientFailureKind.LocalConfiguration, _ => ClientFailureKind.None };
                var state = failure != ClientFailureKind.None ? DownloadStatus.Failed : complete ? DownloadStatus.Completed
                    : ClientHttp.Number(x, "status") == 0 ? DownloadStatus.Paused : DownloadStatus.Downloading;
                var files = x.GetProperty("files").EnumerateArray().Select(f => ClientHttp.Text(f, "name").Replace('\\', '/')).ToArray();
                var directory = ClientHttp.Text(x, "downloadDir").TrimEnd('/', '\\');
                var first = files.FirstOrDefault();
                string? content = null;
                if (files.Length == 1) content = directory + "/" + first;
                else if (first?.Contains('/') == true && files.All(f => f.StartsWith(first.Split('/')[0] + "/", StringComparison.Ordinal)))
                    content = directory + "/" + first.Split('/')[0];
                return new ClientDownload(ClientHttp.Text(x, "hashString"), ClientHttp.Text(x, "name"), state, size, Math.Max(0, size - remaining),
                    Math.Max(0, ClientHttp.Number(x, "rateDownload")), ClientHttp.Eta(ClientHttp.Number(x, "eta")), content,
                    ClientHttp.Labels(x, "labels"), failure, failure == ClientFailureKind.None ? null : failure == ClientFailureKind.Temporary ? "TrackerFailure" : "ClientStorageError",
                    complete && ClientHttp.Text(x, "isFinished").Equals("True", StringComparison.OrdinalIgnoreCase));
            }).ToArray();
    }

    public async Task<string?> AddAsync(DownloadClient client, string sourceUrl, string title, string submissionToken, CancellationToken cancellationToken)
    {
        ClientHttp.ValidateSource(sourceUrl, true);
        var session = await RpcAsync(client, "session-get", new { fields = new[] { "rpc-version" } }, false, cancellationToken);
        if (ClientHttp.Number(session, "rpc-version") < 17) throw new DownloadClientException("Transmission4Required");
        var result = await RpcAsync(client, "torrent-add", new { filename = sourceUrl, labels = new[] { "ludus", submissionToken }, paused = false }, true, cancellationToken);
        if (result.TryGetProperty("torrent-duplicate", out _)) throw new DownloadClientException("DuplicateTorrent");
        return result.TryGetProperty("torrent-added", out var torrent) ? ClientHttp.Text(torrent, "hashString") : throw new DownloadClientException("InvalidResponse", true);
    }

    public async Task RemoveAsync(DownloadClient client, string id, bool deleteData, CancellationToken cancellationToken)
    {
        if (!(await GetDownloadsAsync(client, cancellationToken)).Any(x => x.Id == id)) return;
        await RpcAsync(client, "torrent-remove", new Dictionary<string, object> { ["ids"] = new[] { id }, ["delete-local-data"] = deleteData }, true, cancellationToken);
    }

    public async Task MarkImportedAsync(DownloadClient client, string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(client.PostImportTag)) return;
        var download = (await GetDownloadsAsync(client, cancellationToken)).SingleOrDefault(x => x.Id == id)
            ?? throw new DownloadClientException("CategoryOwnershipLost");
        await RpcAsync(client, "torrent-set", new { ids = new[] { id }, labels = download.Labels.Append(client.PostImportTag).Distinct().ToArray() }, true, cancellationToken);
    }

    private async Task<JsonElement> RpcAsync(DownloadClient client, string method, object arguments, bool mutation, CancellationToken cancellationToken)
    {
        string? session = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ClientHttp.Endpoint(client, "/transmission/rpc"))
            {
                Content = JsonContent.Create(new { method, arguments })
            };
            if (session is not null) request.Headers.Add("X-Transmission-Session-Id", session);
            if (client.Username is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(http.Secret(client.Username) + ":" + http.Secret(client.Password))));
            using var response = await http.SendAsync(request, mutation, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict && response.Headers.TryGetValues("X-Transmission-Session-Id", out var sessions))
            {
                session = sessions.First();
                continue;
            }
            var json = await ClientHttp.JsonAsync(response, cancellationToken);
            if (ClientHttp.Text(json, "result") != "success") throw new DownloadClientException("RpcRejected");
            return json.GetProperty("arguments").Clone();
        }
        throw new DownloadClientException("SessionNegotiationFailed");
    }
}
