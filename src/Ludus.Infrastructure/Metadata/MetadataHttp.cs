using System.Text.Json;
using Ludus.Application.Downloads;
using Ludus.Application.Metadata;
using Ludus.Infrastructure.DownloadClients;

namespace Ludus.Infrastructure.Metadata;

public sealed class MetadataHttp(ClientHttp http)
{
    public async Task<JsonElement> JsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAsync(request, false, cancellationToken);
            return await ClientHttp.JsonAsync(response, cancellationToken);
        }
        catch (DownloadClientException exception) { throw new MetadataException(exception.Code); }
    }
    public static string Text(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var child)
        ? child.ValueKind == JsonValueKind.Object ? Text(child, "text") : child.ToString() : "";
    public static JsonElement Child(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var child) ? child : default;
    public static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    public static int Number(JsonElement value, string field) => int.TryParse(Text(value, field), out var number) ? number : 0;
}

public sealed class MetadataGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public void Dispose() => Semaphore.Dispose();
}
