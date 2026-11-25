using Ludus.Application.Profiles;
using Ludus.Application.Releases;
using Ludus.Domain.Activity;
using Ludus.Domain.Common;
using Ludus.Domain.Library;
using Ludus.Domain.Profiles;

namespace Ludus.Infrastructure.Tests;

public sealed class ScoringTests
{
    private readonly ReleaseParser _parser = new();
    private readonly ReleaseScoringEngine _engine = new(new CustomFormatEvaluator());

    [Theory]
    [InlineData("Example (Europe).chd", 150)]
    [InlineData("Example (USA).iso", 80)]
    [InlineData("Example (Europe) (Beta).iso", -9900)]
    public void CalculatesProfileSpecificFormatScores(string title, int expected)
        => Assert.Equal(expected, _engine.Evaluate(_parser.Parse(title), Context()).Score);

    [Fact]
    public void KeepsAllApplicableRejectionReasons()
    {
        var context = Context();
        context.Profile.ExcludedRegions.Add("Japan");
        var decision = _engine.Evaluate(_parser.Parse("Example (Japan) (Beta) (Demo).nsp"), context);
        Assert.False(decision.Accepted);
        Assert.Contains(decision.Rejections, x => x.Code == RejectionCode.RegionExcluded);
        Assert.Contains(decision.Rejections, x => x.Code == RejectionCode.BetaRejected);
        Assert.Contains(decision.Rejections, x => x.Code == RejectionCode.DemoRejected);
        Assert.Contains(decision.Rejections, x => x.Code == RejectionCode.FormatNotAllowed);
        Assert.Contains(decision.Rejections, x => x.Code == RejectionCode.BelowMinimumScore);
        Assert.All(decision.Rejections, x => { Assert.NotEmpty(x.MessageDe); Assert.NotEmpty(x.MessageEn); });
    }

    [Theory]
    [InlineData(80, true, true, null)]
    [InlineData(100, true, false, RejectionCode.CutoffReached)]
    [InlineData(80, false, false, RejectionCode.UpgradesDisabled)]
    public void EnforcesUpgradeCutoff(int currentScore, bool allowUpgrades, bool accepted, RejectionCode? expected)
    {
        var context = Context() with { ExistingFiles = [Existing(currentScore)] };
        context.Profile.AllowUpgrades = allowUpgrades;
        var result = _engine.Evaluate(_parser.Parse("Example (Europe).iso"), context);
        Assert.Equal(accepted, result.Accepted);
        if (expected is not null) Assert.Contains(result.Rejections, x => x.Code == expected);
    }

    [Fact]
    public void EqualOrLowerScoresAreNotUpgradesAndMissingFilesDoNotBlock()
    {
        var file = Existing(80);
        var context = Context() with { ExistingFiles = [file] };
        Assert.Contains(_engine.Evaluate(_parser.Parse("Example (USA).iso"), context).Rejections, x => x.Code == RejectionCode.NotAnUpgrade);
        file.Missing = true;
        Assert.True(_engine.Evaluate(_parser.Parse("Example (USA).iso"), context).Accepted);
    }

    [Fact]
    public void ABaseGameAtCutoffDoesNotBlockNewDlc()
    {
        var context = Context() with { ExistingFiles = [Existing(500)] };
        context.Platform.AllowedExtensions.Add(".nsp");
        var result = _engine.Evaluate(_parser.Parse("Example (Europe) [01007EF00011F001].nsp"), context);
        Assert.True(result.Accepted);
    }

    [Fact]
    public void CooldownExpiresAtTheExactBoundaryButPermanentBlockDoesNot()
    {
        var now = DateTime.UtcNow;
        var block = new ReleaseBlock { ReleaseFingerprint = "abc", ReleaseTitle = "Example", ReasonCode = "network", Kind = BlockKind.Cooldown, ExpiresAtUtc = now };
        var context = Context() with { NowUtc = now, ReleaseFingerprint = "abc", Blocks = [block] };
        Assert.True(_engine.Evaluate(_parser.Parse("Example (Europe).iso"), context).Accepted);
        block.ExpiresAtUtc = now.AddSeconds(1);
        Assert.Contains(_engine.Evaluate(_parser.Parse("Example (Europe).iso"), context).Rejections, x => x.Code == RejectionCode.Cooldown);
        block.Kind = BlockKind.Permanent;
        block.ExpiresAtUtc = null;
        Assert.Contains(_engine.Evaluate(_parser.Parse("Example (Europe).iso"), context).Rejections, x => x.Code == RejectionCode.Blocklisted);
    }

    [Fact]
    public void InvalidRegexFailsClosedWithoutLosingTheRelease()
    {
        var context = Context();
        context.Profile.Formats.Add(Format("Broken", FormatField.ReleaseTitle, "[", 1000, FormatOperator.Regex));
        var result = _engine.Evaluate(_parser.Parse("Example (Europe).iso"), context);
        Assert.Equal(100, result.Score);
        Assert.Contains(result.Rejections, x => x.Code == RejectionCode.InvalidCustomFormat);
    }

    [Fact]
    public void RequiredAndNegatedRulesAndNumericVersionsAreRespected()
    {
        var format = new CustomFormat
        {
            Name = "Revision", MatchMode = FormatMatchMode.All,
            Rules =
            [
                new CustomFormatRule { Field = FormatField.Region, Operator = FormatOperator.Equals, Value = "Europe", Required = true },
                new CustomFormatRule { Field = FormatField.Status, Operator = FormatOperator.Equals, Value = "Beta", Negate = true },
                new CustomFormatRule { Field = FormatField.Revision, Operator = FormatOperator.GreaterThanOrEqual, Value = "1.2" }
            ]
        };
        var evaluator = new CustomFormatEvaluator();
        Assert.True(evaluator.Matches(format, _parser.Parse("Example (Europe) (v1.10).iso")));
        Assert.False(evaluator.Matches(format, _parser.Parse("Example (Europe) (v1.1).iso")));
        Assert.False(evaluator.Matches(format, _parser.Parse("Example (Europe) (Beta) (v1.10).iso")));
        format.MatchMode = FormatMatchMode.Any;
        Assert.False(evaluator.Matches(format, _parser.Parse("Example (USA) (v1.10).iso")));
    }

    [Fact]
    public void SequelsAreRejectedAndManualSearchDoesNotRequireMonitoring()
    {
        var context = Context() with { Game = new Game { Title = "Example", SortTitle = "Example", Monitored = false } };
        Assert.True(_engine.Evaluate(_parser.Parse("Example (Europe).iso"), context).Accepted);
        Assert.Contains(_engine.Evaluate(_parser.Parse("Example 2 (Europe).iso"), context).Rejections, x => x.Code == RejectionCode.TitleMismatch);
        Assert.Contains(_engine.Evaluate(_parser.Parse("Example (Europe).iso"), context with { Automatic = true }).Rejections, x => x.Code == RejectionCode.NotMonitored);
    }

    [Fact]
    public void ExistingDiscAtCutoffDoesNotRejectAnotherMissingDisc()
    {
        var disc = Existing(100);
        disc.DiscNumber = 1;
        var context = Context() with { ExistingFiles = [disc] };
        Assert.True(_engine.Evaluate(_parser.Parse("Example (Europe) (Disc 2).iso"), context).Accepted);
        Assert.Contains(_engine.Evaluate(_parser.Parse("Example (Europe) (Disc 1).iso"), context).Rejections,
            x => x.Code == RejectionCode.CutoffReached);
    }

    [Fact]
    public void AnOwnedDlcDoesNotBlockAnotherDlcWithADifferentTitleId()
    {
        var owned = Existing(500);
        owned.ContentKind = ContentKind.Dlc;
        owned.TitleId = "01007EF00011F001";
        var context = Context() with { ExistingFiles = [owned] };
        context.Platform.AllowedExtensions.Add(".nsp");
        Assert.True(_engine.Evaluate(_parser.Parse("Example (Europe) [01007EF00011F002].nsp"), context).Accepted);
        Assert.Contains(_engine.Evaluate(_parser.Parse("Example (Europe) [01007EF00011F001].nsp"), context).Rejections,
            x => x.Code == RejectionCode.CutoffReached);
    }

    private static ReleaseEvaluationContext Context() => new(
        new QualityProfile
        {
            Name = "Test", MinimumScore = 0, UpgradeUntilScore = 100,
            Formats = [Format("Europe", FormatField.Region, "Europe", 100), Format("USA", FormatField.Region, "USA", 80),
                Format("CHD", FormatField.Extension, ".chd", 50), Format("Beta", FormatField.Status, "Beta", -10000)]
        }, new Platform { Name = "PS1", Slug = "ps1", AllowedExtensions = [".iso", ".chd"] });

    private static QualityProfileFormat Format(string name, FormatField field, string value, int score, FormatOperator op = FormatOperator.Equals)
        => new() { Score = score, CustomFormat = new CustomFormat { Name = name, Rules = [new CustomFormatRule { Field = field, Value = value, Operator = op }] } };

    private static GameFile Existing(int score) => new()
    {
        RelativePath = "Example/file.iso", OriginalFileName = "file.iso", ReleaseName = "Example (USA)", Score = score, ContentKind = ContentKind.Base
    };
}
