using Ludus.Domain.Common;
using Ludus.Domain.Integrations;
using Ludus.Domain.Library;

namespace Ludus.Domain.Activity;

public sealed class ReleaseBlock : Entity
{
    public long GameId { get; set; }
    public Game Game { get; set; } = null!;
    public long? IndexerId { get; set; }
    public Indexer? Indexer { get; set; }
    public required string ReleaseFingerprint { get; set; }
    public required string ReleaseTitle { get; set; }
    public required string ReasonCode { get; set; }
    public BlockKind Kind { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}
