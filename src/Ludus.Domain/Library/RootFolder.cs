using Ludus.Domain.Common;

namespace Ludus.Domain.Library;

public sealed class RootFolder : Entity
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public DateTime? LastScanAtUtc { get; set; }
}
