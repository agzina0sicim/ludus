using Ludus.Application.Releases;
using Ludus.Domain.Activity;
using Ludus.Domain.Library;
using Ludus.Domain.Profiles;

namespace Ludus.Application.Profiles;

public enum RejectionCode
{
    RegionExcluded, RegionNotAllowed, BetaRejected, DemoRejected, PrototypeRejected, FormatNotAllowed,
    BelowMinimumScore, UpgradesDisabled, CutoffReached, NotAnUpgrade, Blocklisted, Cooldown,
    UpdatesDisabled, DlcDisabled, InvalidCustomFormat, AmbiguousRelease, TitleMismatch, NotMonitored,
    InsufficientSeeders, DownloadUnavailable, CategoryMismatch
}

public sealed record RejectionReason(RejectionCode Code, string MessageDe, string MessageEn, string? Detail = null);
public sealed record FormatScore(long CustomFormatId, string Name, int Score);
public sealed record ReleaseDecision(int Score, IReadOnlyList<FormatScore> Formats, IReadOnlyList<RejectionReason> Rejections)
{
    public bool Accepted => Rejections.Count == 0;
}

public sealed record ReleaseEvaluationContext(QualityProfile Profile, Platform Platform)
{
    public Game? Game { get; init; }
    public IReadOnlyList<GameFile> ExistingFiles { get; init; } = [];
    public IReadOnlyList<ReleaseBlock> Blocks { get; init; } = [];
    public string? ReleaseFingerprint { get; init; }
    public DateTime NowUtc { get; init; } = DateTime.UtcNow;
    public bool Automatic { get; init; }
}

public static class RejectionReasons
{
    public static RejectionReason Create(RejectionCode code, string? detail = null)
    {
        var (de, en) = code switch
        {
            RejectionCode.RegionExcluded => ("Region ausgeschlossen", "Region excluded"),
            RejectionCode.RegionNotAllowed => ("Keine erlaubte Region erkannt", "No allowed region found"),
            RejectionCode.BetaRejected => ("Beta abgelehnt", "Beta rejected"),
            RejectionCode.DemoRejected => ("Demo abgelehnt", "Demo rejected"),
            RejectionCode.PrototypeRejected => ("Prototyp abgelehnt", "Prototype rejected"),
            RejectionCode.FormatNotAllowed => ("Dateiendung passt nicht zur Plattform", "File extension is not allowed for this platform"),
            RejectionCode.BelowMinimumScore => ("Mindest-Score nicht erreicht", "Below minimum score"),
            RejectionCode.UpgradesDisabled => ("Upgrades deaktiviert", "Upgrades disabled"),
            RejectionCode.CutoffReached => ("Upgrade-Cutoff bereits erreicht", "Upgrade cutoff already reached"),
            RejectionCode.NotAnUpgrade => ("Kein besseres Release", "Release is not an upgrade"),
            RejectionCode.Blocklisted => ("Release dauerhaft gesperrt", "Release permanently blocklisted"),
            RejectionCode.Cooldown => ("Release im Cooldown", "Release on cooldown"),
            RejectionCode.UpdatesDisabled => ("Updates deaktiviert", "Updates disabled"),
            RejectionCode.DlcDisabled => ("DLCs deaktiviert", "DLC disabled"),
            RejectionCode.InvalidCustomFormat => ("Ungültige Custom-Format-Regel", "Invalid custom format rule"),
            RejectionCode.AmbiguousRelease => ("Release nicht eindeutig zuordenbar", "Release cannot be identified unambiguously"),
            RejectionCode.TitleMismatch => ("Release gehört nicht zu diesem Spiel", "Release does not match this game"),
            RejectionCode.NotMonitored => ("Spiel oder Plattform nicht überwacht", "Game or platform is not monitored"),
            RejectionCode.InsufficientSeeders => ("Nicht genügend Seeder", "Insufficient seeders"),
            RejectionCode.DownloadUnavailable => ("Kein gültiger Download-Link", "No valid download link"),
            RejectionCode.CategoryMismatch => ("Indexer-Kategorie passt nicht zur Plattform", "Indexer category does not match the platform"),
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };
        return new RejectionReason(code, de, en, detail);
    }
}
