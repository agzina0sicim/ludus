using System.Text.RegularExpressions;
using Ludus.Application.Releases;
using Ludus.Domain.Common;

namespace Ludus.Application.Profiles;

public sealed class ReleaseScoringEngine(CustomFormatEvaluator formatEvaluator)
{
    public ReleaseDecision Evaluate(ParsedRelease release, ReleaseEvaluationContext context)
    {
        var reasons = new List<RejectionReason>();
        var formats = new List<FormatScore>();
        var profile = context.Profile;
        if (profile.Formats.Count > 1000) throw new ArgumentException("Too many custom formats in profile.");
        foreach (var entry in profile.Formats)
        {
            try
            {
                if (formatEvaluator.Matches(entry.CustomFormat, release))
                    formats.Add(new FormatScore(entry.CustomFormatId, entry.CustomFormat.Name, entry.Score));
            }
            catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException)
            {
                reasons.Add(RejectionReasons.Create(RejectionCode.InvalidCustomFormat, entry.CustomFormat.Name));
            }
        }
        var total = formats.Sum(x => (long)x.Score);
        var score = (int)Math.Clamp(total, int.MinValue, int.MaxValue);
        if (total != score) reasons.Add(RejectionReasons.Create(RejectionCode.InvalidCustomFormat, "ScoreOverflow"));
        void Reject(bool condition, RejectionCode code)
        {
            if (condition) reasons.Add(RejectionReasons.Create(code));
        }
        Reject(release.Warnings.Count > 0, RejectionCode.AmbiguousRelease);
        Reject(release.Regions.Intersect(profile.ExcludedRegions, StringComparer.OrdinalIgnoreCase).Any(), RejectionCode.RegionExcluded);
        Reject(profile.AllowedRegions.Count > 0 && !release.Regions.Intersect(profile.AllowedRegions, StringComparer.OrdinalIgnoreCase).Any(), RejectionCode.RegionNotAllowed);
        Reject(profile.RejectBeta && release.StatusTags.Contains("Beta"), RejectionCode.BetaRejected);
        Reject(profile.RejectDemo && release.StatusTags.Contains("Demo"), RejectionCode.DemoRejected);
        Reject(profile.RejectPrototype && release.StatusTags.Contains("Proto"), RejectionCode.PrototypeRejected);
        Reject(release.Extension is not null && !context.Platform.AllowedExtensions.Contains(release.Extension, StringComparer.OrdinalIgnoreCase), RejectionCode.FormatNotAllowed);
        Reject(score < profile.MinimumScore, RejectionCode.BelowMinimumScore);
        Reject(release.ContentKind == ContentKind.Update && !profile.AllowUpdates, RejectionCode.UpdatesDisabled);
        Reject(release.ContentKind == ContentKind.Dlc && !profile.AllowDlc, RejectionCode.DlcDisabled);
        Reject(context.Automatic && (!context.Platform.Monitored || context.Game is { Monitored: false }), RejectionCode.NotMonitored);
        if (context.Game is { } game)
        {
            var matches = release.SwitchIdentity is not null && game.BaseTitleId is not null
                ? string.Equals(release.SwitchIdentity.BaseTitleId, game.BaseTitleId, StringComparison.OrdinalIgnoreCase)
                : release.Serials.Count > 0 && game.Serials.Count > 0
                    ? release.Serials.Intersect(game.Serials, StringComparer.OrdinalIgnoreCase).Any()
                    : ReleaseParser.NormalizeTitle(release.Title) == ReleaseParser.NormalizeTitle(game.Title);
            Reject(!matches, RejectionCode.TitleMismatch);
        }
        foreach (var block in context.Blocks.Where(x => x.ReleaseFingerprint == context.ReleaseFingerprint
            && (context.Game is null || x.GameId == context.Game.Id)))
        {
            Reject(block.Kind == BlockKind.Permanent, RejectionCode.Blocklisted);
            Reject(block.Kind == BlockKind.Cooldown && block.ExpiresAtUtc > context.NowUtc, RejectionCode.Cooldown);
        }

        var existing = context.ExistingFiles.Where(x => !x.Missing && x.ContentKind == release.ContentKind
            && x.Role is FileRole.Rom or FileRole.Directory or FileRole.Descriptor
            && (context.Game is null || x.GameId == context.Game.Id)).ToArray();
        if (release.DiscNumber is not null)
            existing = existing.Where(x => x.DiscNumber is null || x.DiscNumber == release.DiscNumber).ToArray();
        if (release.ContentKind != ContentKind.Base)
        {
            if (release.SwitchIdentity is not null)
                existing = existing.Where(x => string.Equals(x.TitleId, release.SwitchIdentity.TitleId, StringComparison.OrdinalIgnoreCase)).ToArray();
            else if (release.ContentKind == ContentKind.Dlc && existing.Length > 0)
                Reject(true, RejectionCode.AmbiguousRelease);
        }
        if (existing.Length > 0)
        {
            var currentScore = existing.Max(x => x.Score);
            Reject(!profile.AllowUpgrades, RejectionCode.UpgradesDisabled);
            Reject(currentScore >= profile.UpgradeUntilScore, RejectionCode.CutoffReached);
            Reject(score <= currentScore, RejectionCode.NotAnUpgrade);
        }
        return new ReleaseDecision(score, formats, reasons.DistinctBy(x => (x.Code, x.Detail)).ToArray());
    }
}
