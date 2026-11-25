using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ludus.Application.Downloads;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;

namespace Ludus.Infrastructure.DownloadClients;

public sealed partial class SabnzbdAdapter(ClientHttp http) : IDownloadClientAdapter
{
    public DownloadClientKind Kind => DownloadClientKind.Sabnzbd;

    public async Task<IReadOnlyList<ClientDownload>> GetDownloadsAsync(DownloadClient client, CancellationToken cancellationToken)
    {
        var downloads = new List<ClientDownload>();
        foreach (var mode in new[] { "queue", "history" })
        {
            for (var offset = 0; offset < 10000; offset += 100)
            {
                var json = await CallAsync(client, new() { ["mode"] = mode, ["cat"] = "ludus", ["start"] = offset.ToString(CultureInfo.InvariantCulture), ["limit"] = "100" }, false, cancellationToken);
                var slots = json.GetProperty(mode).GetProperty("slots").EnumerateArray().ToArray();
                foreach (var item in slots.Where(x => ClientHttp.Text(x, "cat") == "ludus"))
                {
                    var name = ClientHttp.Text(item, "name", ClientHttp.Text(item, "filename"));
                    var state = ClientHttp.Text(item, "status");
                    var size = mode == "history" ? ClientHttp.Number(item, "bytes") : (long)(ClientHttp.Decimal(item, "mb") * 1024 * 1024);
                    var failureText = ClientHttp.Text(item, "fail_message");
                    var failure = state == "Failed" ? Failure(failureText) : ClientFailureKind.None;
                    var status = state switch { "Completed" => DownloadStatus.Completed, "Failed" => DownloadStatus.Failed, "Paused" => DownloadStatus.Paused, _ => DownloadStatus.Downloading };
                    var seconds = TimeSpan.TryParse(ClientHttp.Text(item, "timeleft"), CultureInfo.InvariantCulture, out var eta) ? (long?)Math.Max(0, eta.TotalSeconds) : null;
                    var bytesLeft = mode == "history" ? 0 : (long)(ClientHttp.Decimal(item, "mbleft") * 1024 * 1024);
                    var tags = SubmissionPattern().Matches(name).Select(x => x.Value).Concat(ClientHttp.Labels(item, "labels")).Distinct().ToArray();
                    downloads.Add(new ClientDownload(ClientHttp.Text(item, "nzo_id"), name, status, Math.Max(0, size), Math.Max(0, size - bytesLeft),
                        0, seconds, mode == "history" ? ClientHttp.Text(item, "storage") : null, tags, failure,
                        failure == ClientFailureKind.None ? null : failure == ClientFailureKind.InvalidContent ? "InvalidArchive" : failure == ClientFailureKind.LocalConfiguration ? "ClientStorageError" : "DownloadFailed",
                        status == DownloadStatus.Completed));
                }
                if (slots.Length < 100) break;
                if (offset == 9900) throw new DownloadClientException("QueueLimitExceeded");
            }
        }
        return downloads.DistinctBy(x => x.Id).ToArray();
    }

    public async Task<string?> AddAsync(DownloadClient client, string sourceUrl, string title, string submissionToken, CancellationToken cancellationToken)
    {
        ClientHttp.ValidateSource(sourceUrl, false);
        var json = await CallAsync(client, new() { ["mode"] = "addurl", ["name"] = sourceUrl, ["nzbname"] = $"{title} [{submissionToken}]", ["cat"] = "ludus", ["pp"] = "3" }, true, cancellationToken);
        return json.TryGetProperty("nzo_ids", out var ids) && ids.GetArrayLength() == 1 ? ids[0].GetString() : throw new DownloadClientException("AddUnconfirmed", true);
    }

    public async Task RemoveAsync(DownloadClient client, string id, bool deleteData, CancellationToken cancellationToken)
    {
        var download = (await GetDownloadsAsync(client, cancellationToken)).SingleOrDefault(x => x.Id == id);
        if (download is null) return;
        var history = download.Status is DownloadStatus.Completed or DownloadStatus.Failed;
        await CallAsync(client, new() { ["mode"] = history ? "history" : "queue", ["name"] = "delete", ["value"] = id, ["del_files"] = deleteData ? "1" : "0" }, true, cancellationToken);
    }

    public async Task MarkImportedAsync(DownloadClient client, string id, CancellationToken cancellationToken)
    {
        // SABnzbd has no mutable arbitrary history tags; Ludus records import state in its database.
        if (!(await GetDownloadsAsync(client, cancellationToken)).Any(x => x.Id == id)) throw new DownloadClientException("CategoryOwnershipLost");
    }

    private async Task<JsonElement> CallAsync(DownloadClient client, Dictionary<string, string> parameters, bool mutation, CancellationToken cancellationToken)
    {
        parameters["apikey"] = http.Secret(client.ApiKey);
        parameters["output"] = "json";
        using var request = new HttpRequestMessage(HttpMethod.Post, ClientHttp.Endpoint(client, "/api")) { Content = new FormUrlEncodedContent(parameters) };
        using var response = await http.SendAsync(request, mutation, cancellationToken);
        var json = await ClientHttp.JsonAsync(response, cancellationToken);
        if (json.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.False) throw new DownloadClientException("ApiRejected");
        if (json.TryGetProperty("error", out _)) throw new DownloadClientException("ApiRejected");
        return json;
    }

    private static ClientFailureKind Failure(string message)
    {
        if (new[] { "disk", "space", "permission", "folder" }.Any(word => message.Contains(word, StringComparison.OrdinalIgnoreCase))) return ClientFailureKind.LocalConfiguration;
        if (new[] { "unpack", "archive", "crc", "corrupt", "repair failed", "password" }.Any(word => message.Contains(word, StringComparison.OrdinalIgnoreCase))) return ClientFailureKind.InvalidContent;
        return ClientFailureKind.Temporary;
    }

    [GeneratedRegex(@"ludus-[0-9a-f]{32}", RegexOptions.CultureInvariant, 100)]
    private static partial Regex SubmissionPattern();
}
