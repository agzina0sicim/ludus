using Ludus.Application.Releases;
using Ludus.Domain.Common;

namespace Ludus.Infrastructure.Tests;

public sealed class ReleaseParserTests
{
    private readonly ReleaseParser _parser = new();

    [Fact]
    public void ExtractsRegionsLanguagesRevisionDiscAndStatus()
    {
        var result = _parser.Parse("Example (USA, Europe) (En,Fr,De) (Rev 1) (Disc 2 of 3) [!].chd");
        Assert.Equal("Example", result.Title);
        Assert.Equal(["USA", "Europe"], result.Regions);
        Assert.Equal(["En", "Fr", "De"], result.Languages);
        Assert.Equal("1", result.Revision);
        Assert.Equal(2, result.DiscNumber);
        Assert.Equal(3, result.DiscCount);
        Assert.Equal(".chd", result.Extension);
        Assert.Contains("VerifiedTag", result.StatusTags);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("SLUS_123.45", "SLUS-12345")]
    [InlineData("SCES-12345", "SCES-12345")]
    [InlineData("SLES12345", "SLES-12345")]
    [InlineData("CUSA 12345", "CUSA-12345")]
    [InlineData("PPSA12345", "PPSA-12345")]
    public void RemovesPlayStationSerialsBeforeMatching(string input, string expected)
    {
        var result = _parser.Parse($"Example [{input}] (Europe).iso");
        Assert.Equal("Example", result.Title);
        Assert.Equal(expected, Assert.Single(result.Serials));
    }

    [Theory]
    [InlineData("01007EF00011E000", ContentKind.Base)]
    [InlineData("01007EF00011E800", ContentKind.Update)]
    [InlineData("01007EF00011F001", ContentKind.Dlc)]
    [InlineData("01007ef00011f002", ContentKind.Dlc)]
    [InlineData("01007EF00011F800", ContentKind.Dlc)]
    [InlineData("01007EF00011FFFF", ContentKind.Dlc)]
    public void SwitchIdentityResolvesTheSameBaseWithBitmasks(string id, ContentKind kind)
    {
        var result = _parser.Parse($"Example [{id}].nsp");
        Assert.Equal("Example", result.Title);
        Assert.Equal(kind, result.ContentKind);
        Assert.Equal("01007EF00011E000", result.SwitchIdentity!.BaseTitleId);
        Assert.Equal(id.ToUpperInvariant(), result.SwitchIdentity.TitleId);
    }

    [Fact]
    public void ConflictingIdsOrContentTagsAreNotSilentlyAssigned()
    {
        var multiple = _parser.Parse("Example [01007EF00011E000] [01007EF00011E800].nsp");
        Assert.Null(multiple.SwitchIdentity);
        Assert.Contains("MultipleTitleIds", multiple.Warnings);
        var conflicting = _parser.Parse("Example [01007EF00011E000] [DLC].nsp");
        Assert.Contains("ConflictingContentKinds", conflicting.Warnings);
    }

    [Theory]
    [InlineData("(Beta)", "Beta")]
    [InlineData("(Beta 2)", "Beta")]
    [InlineData("(Demo)", "Demo")]
    [InlineData("(Proto)", "Proto")]
    [InlineData("(Unl)", "Unl")]
    [InlineData("(Aftermarket)", "Aftermarket")]
    [InlineData("[Hack]", "Hack")]
    public void ParsesReleaseStatusTags(string tag, string status)
    {
        var result = _parser.Parse($"Example {tag}.zip");
        Assert.Equal("Example", result.Title);
        Assert.Contains(status, result.StatusTags);
    }

    [Theory]
    [InlineData("[T-Ger]", "De", "TranslationPartial")]
    [InlineData("[T+Eng]", "En", "TranslationComplete")]
    public void ParsesFanTranslations(string tag, string language, string status)
    {
        var result = _parser.Parse($"Example {tag}.sfc");
        Assert.Contains(language, result.Languages);
        Assert.Contains(status, result.StatusTags);
    }

    [Theory]
    [InlineData("[Update]", ContentKind.Update)]
    [InlineData("[Patch]", ContentKind.Update)]
    [InlineData("[DLC]", ContentKind.Dlc)]
    public void ParsesAdditionalContentWithoutTitleId(string tag, ContentKind kind)
        => Assert.Equal(kind, _parser.Parse($"Example {tag}.zip").ContentKind);

    [Fact]
    public void PreservesUnknownParenthesesAndMeaningfulTitlePunctuation()
    {
        var result = _parser.Parse("Game (Special Edition) - Part II (Germany) (v1.10).7z");
        Assert.Equal("Game (Special Edition) - Part II", result.Title);
        Assert.Equal("1.10", result.Revision);
        Assert.Equal(["Germany"], result.Regions);
    }

    [Fact]
    public void RejectsOversizedInputAndDoesNotMisreadSystemTitleIds()
    {
        Assert.Throws<ArgumentException>(() => _parser.Parse(new string('a', 4097)));
        Assert.False(ReleaseParser.TryParseSwitchTitleId("0100000000000001", out _));
        Assert.False(ReleaseParser.TryParseSwitchTitleId("01007EF00011E00", out _));
        Assert.False(ReleaseParser.TryParseSwitchTitleId("01007EF00011E001", out _));
        Assert.False(ReleaseParser.TryParseSwitchTitleId("01007EF00011F000", out _));
    }

    [Fact]
    public void NormalizesTrailingArticlesWithoutDroppingSequelNumbers()
    {
        Assert.Equal(ReleaseParser.NormalizeTitle("The Example"), ReleaseParser.NormalizeTitle("Example, The"));
        Assert.NotEqual(ReleaseParser.NormalizeTitle("Example"), ReleaseParser.NormalizeTitle("Example 2"));
    }
}
