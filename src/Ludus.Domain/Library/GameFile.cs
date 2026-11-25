using Ludus.Domain.Common;
using Ludus.Domain.Verification;

namespace Ludus.Domain.Library;

public sealed class GameFile : Entity
{
    public long GameId { get; set; }
    public Game Game { get; set; } = null!;
    public required string RelativePath { get; set; }
    public required string OriginalFileName { get; set; }
    public required string ReleaseName { get; set; }
    public ContentKind ContentKind { get; set; }
    public FileRole Role { get; set; } = FileRole.Rom;
    public Guid FileGroupId { get; set; }
    public int? DiscNumber { get; set; }
    public int Score { get; set; }
    public long Size { get; set; }
    public string? Crc32 { get; set; }
    public string? Md5 { get; set; }
    public string? Sha1 { get; set; }
    public string? TitleId { get; set; }
    public string? Revision { get; set; }
    public List<string> Regions { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public VerificationStatus VerificationStatus { get; set; }
    public long? DatRomId { get; set; }
    public DatRom? DatRom { get; set; }
    public DateTime ImportedAtUtc { get; set; }
    public DateTime? LastVerifiedAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
    public bool Missing { get; set; }
}
