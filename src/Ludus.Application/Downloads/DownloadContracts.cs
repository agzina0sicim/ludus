using Ludus.Domain.Common;
using Ludus.Domain.Integrations;

namespace Ludus.Application.Downloads;

public enum ClientFailureKind { None, Temporary, InvalidContent, LocalConfiguration }
public sealed record ClientDownload(string Id, string Name, DownloadStatus Status, long Size, long DownloadedBytes,
    long SpeedBytesPerSecond, long? EtaSeconds, string? ContentPath, IReadOnlyList<string> Labels,
    ClientFailureKind FailureKind = ClientFailureKind.None, string? FailureCode = null, bool CanRemove = false);
public sealed class DownloadClientException(string code, bool uncertain = false) : Exception($"Download client error: {code}.")
{
    public string Code { get; } = code;
    public bool Uncertain { get; } = uncertain;
}
public interface IDownloadClientAdapter
{
    DownloadClientKind Kind { get; }
    Task<IReadOnlyList<ClientDownload>> GetDownloadsAsync(DownloadClient client, CancellationToken cancellationToken);
    Task<string?> AddAsync(DownloadClient client, string sourceUrl, string title, string submissionToken, CancellationToken cancellationToken);
    Task RemoveAsync(DownloadClient client, string id, bool deleteData, CancellationToken cancellationToken);
    Task MarkImportedAsync(DownloadClient client, string id, CancellationToken cancellationToken);
}

public interface IRealtimePublisher
{
    Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default);
}

public sealed record QueueEvent(long DownloadId, long GameId, DownloadStatus Status, long Size, long DownloadedBytes,
    long SpeedBytesPerSecond, long? EtaSeconds, string? FailureCode);
