using Ludus.Domain.Common;
using Ludus.Domain.Library;

namespace Ludus.Domain.Verification;

public sealed class DatFile : Entity
{
    public long PlatformId { get; set; }
    public Platform Platform { get; set; } = null!;
    public required string Name { get; set; }
    public required string FileName { get; set; }
    public required string Sha256 { get; set; }
    public string? Description { get; set; }
    public string? DatVersion { get; set; }
    public string? Author { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime ImportedAtUtc { get; set; }
    public ICollection<DatGame> Games { get; set; } = [];
}

public sealed class DatGame : Entity
{
    public long DatFileId { get; set; }
    public DatFile DatFile { get; set; } = null!;
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Serial { get; set; }
    public string? Category { get; set; }
    public ICollection<DatRom> Roms { get; set; } = [];
}

public sealed class DatRom : Entity
{
    public long DatGameId { get; set; }
    public DatGame DatGame { get; set; } = null!;
    public required string Name { get; set; }
    public long? Size { get; set; }
    public string? Crc32 { get; set; }
    public string? Md5 { get; set; }
    public string? Sha1 { get; set; }
    public string? Status { get; set; }
    public string? Merge { get; set; }
}
