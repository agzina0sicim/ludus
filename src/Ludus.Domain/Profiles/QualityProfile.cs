using Ludus.Domain.Common;

namespace Ludus.Domain.Profiles;

public sealed class QualityProfile : Entity
{
    public required string Name { get; set; }
    public int MinimumScore { get; set; }
    public bool AllowUpgrades { get; set; } = true;
    public int UpgradeUntilScore { get; set; } = 100;
    public List<string> AllowedRegions { get; set; } = [];
    public List<string> ExcludedRegions { get; set; } = [];
    public bool RejectBeta { get; set; } = true;
    public bool RejectDemo { get; set; } = true;
    public bool RejectPrototype { get; set; } = true;
    public bool AllowUpdates { get; set; } = true;
    public bool AllowDlc { get; set; } = true;
    public ICollection<QualityProfileFormat> Formats { get; set; } = [];
}

public sealed class CustomFormat : Entity
{
    public required string Name { get; set; }
    public FormatMatchMode MatchMode { get; set; } = FormatMatchMode.All;
    public ICollection<CustomFormatRule> Rules { get; set; } = [];
}

public sealed class CustomFormatRule : Entity
{
    public long CustomFormatId { get; set; }
    public CustomFormat CustomFormat { get; set; } = null!;
    public FormatField Field { get; set; }
    public FormatOperator Operator { get; set; }
    public required string Value { get; set; }
    public bool Negate { get; set; }
    public bool Required { get; set; }
}

public sealed class QualityProfileFormat
{
    public long QualityProfileId { get; set; }
    public QualityProfile QualityProfile { get; set; } = null!;
    public long CustomFormatId { get; set; }
    public CustomFormat CustomFormat { get; set; } = null!;
    public int Score { get; set; }
}
