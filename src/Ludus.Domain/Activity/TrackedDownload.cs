using Ludus.Domain.Common;
using Ludus.Domain.Integrations;
using Ludus.Domain.Library;

namespace Ludus.Domain.Activity;

public sealed class TrackedDownload : Entity
{
    public long GameId { get; set; }
    public Game Game { get; set; } = null!;
    public long DownloadClientId { get; set; }
    public DownloadClient DownloadClient { get; set; } = null!;
    public long? IndexerId { get; set; }
    public Indexer? Indexer { get; set; }
    public required string DownloadId { get; set; }
    public required string ReleaseGuid { get; set; }
    public required string ReleaseFingerprint { get; set; }
    public required string ReleaseTitle { get; set; }
    public ProtectedSecret? SourceUrl { get; set; }
    public DownloadProtocol Protocol { get; set; }
    public ContentKind ContentKind { get; set; }
    public GrabOrigin Origin { get; set; }
    public DownloadStatus Status { get; set; }
    public int Score { get; set; }
    public long Size { get; set; }
    public long DownloadedBytes { get; set; }
    public long SpeedBytesPerSecond { get; set; }
    public long? EtaSeconds { get; set; }
    public string? RemoteContentPath { get; set; }
    public string? FailureCode { get; set; }
    public int ImportAttempts { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ImportedAtUtc { get; set; }
    public string? SubmissionToken { get; set; }
    public bool CleanupPending { get; set; }
    public bool ReplacementSearchPending { get; set; }
    public DateTime? FailureHandledAtUtc { get; set; }
}
