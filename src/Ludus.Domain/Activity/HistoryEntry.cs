using Ludus.Domain.Common;
using Ludus.Domain.Library;

namespace Ludus.Domain.Activity;

public sealed class HistoryEntry : Entity
{
    public long? GameId { get; set; }
    public Game? Game { get; set; }
    public long? DownloadId { get; set; }
    public TrackedDownload? Download { get; set; }
    public required string GameTitle { get; set; }
    public required string Message { get; set; }
    public string? ReleaseTitle { get; set; }
    public HistoryEvent Event { get; set; }
    public ContentKind? ContentKind { get; set; }
    public int? Score { get; set; }
}
