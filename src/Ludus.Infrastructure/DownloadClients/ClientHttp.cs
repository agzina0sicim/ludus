using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Ludus.Application.Downloads;
using Ludus.Application.Security;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;

namespace Ludus.Infrastructure.DownloadClients;

public sealed class ClientHttp(IHttpClientFactory factory, ISecretProtector secrets)
{
    public string Secret(ProtectedSecret? value)
    {
        try { return value is null ? "" : secrets.Unprotect(value); }
        catch (CryptographicException) { throw new DownloadClientException("InvalidCredentials"); }
    }

    public static Uri Endpoint(DownloadClient client, string suffix)
    {
        if (client.Category != DownloadClient.LudusCategory || !Uri.TryCreate(client.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new DownloadClientException("InvalidConfiguration");
        var path = uri.AbsolutePath.TrimEnd('/');
        var ending = suffix.TrimEnd('/');
        var result = path.EndsWith(ending, StringComparison.OrdinalIgnoreCase) ? path : path + ending;
        return new UriBuilder(uri) { Path = suffix.EndsWith('/') ? result + "/" : result }.Uri;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool mutation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var http = factory.CreateClient("DownloadClients");
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            try
            {
                if (response.Content.Headers.ContentLength > 16 * 1024 * 1024) throw new DownloadClientException("ResponseTooLarge", mutation);
                await response.Content.LoadIntoBufferAsync(16 * 1024 * 1024, timeout.Token);
                return response;
            }
            catch { response.Dispose(); throw; }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new DownloadClientException("Timeout", mutation); }
        catch (HttpRequestException) { throw new DownloadClientException("ConnectionFailed", mutation); }
        catch (IOException) { throw new DownloadClientException("ConnectionFailed", mutation); }
    }

    public static void RequireSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new DownloadClientException($"Http{(int)response.StatusCode}", response.StatusCode >= HttpStatusCode.InternalServerError);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        RequireSuccess(response);
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw new DownloadClientException("InvalidResponse", true); }
    }

    public static string Text(JsonElement value, string name, string fallback = "") => value.TryGetProperty(name, out var property) ? property.ToString() : fallback;
    public static long Number(JsonElement value, string name) => long.TryParse(Text(value, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    public static double Decimal(JsonElement value, string name) => double.TryParse(Text(value, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? result : 0;
    public static string[] Labels(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array
        ? property.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [];
    public static string[] Tokens(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static long? Eta(long seconds) => seconds is >= 0 and < 86400000 ? seconds : null;
    public static void ValidateSource(string source, bool torrent)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || !(uri.Scheme is "http" or "https" || (torrent && uri.Scheme == "magnet")))
            throw new DownloadClientException("InvalidSource");
    }
}
