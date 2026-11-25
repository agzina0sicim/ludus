using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ludus.Domain.Common;

namespace Ludus.Application.Releases;

public sealed partial class ReleaseParser
{
    private static readonly Dictionary<string, string> Regions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USA"] = "USA", ["US"] = "USA", ["Europe"] = "Europe", ["EU"] = "Europe",
        ["Japan"] = "Japan", ["JP"] = "Japan", ["World"] = "World", ["Germany"] = "Germany",
        ["France"] = "France", ["Spain"] = "Spain", ["Italy"] = "Italy", ["Australia"] = "Australia",
        ["Korea"] = "Korea", ["China"] = "China", ["Taiwan"] = "Taiwan", ["Brazil"] = "Brazil",
        ["Canada"] = "Canada", ["UK"] = "UK", ["Netherlands"] = "Netherlands", ["Sweden"] = "Sweden",
        ["Russia"] = "Russia", ["Asia"] = "Asia"
    };
    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["En"] = "En", ["Eng"] = "En", ["English"] = "En", ["De"] = "De", ["Ger"] = "De",
        ["German"] = "De", ["Fr"] = "Fr", ["Fre"] = "Fr", ["Es"] = "Es", ["It"] = "It",
        ["Ja"] = "Ja", ["Jp"] = "Ja", ["Nl"] = "Nl", ["Pt"] = "Pt", ["Ru"] = "Ru",
        ["Ko"] = "Ko", ["Zh"] = "Zh", ["Sv"] = "Sv", ["No"] = "No", ["Da"] = "Da",
        ["Fi"] = "Fi", ["Pl"] = "Pl", ["Cs"] = "Cs", ["Hu"] = "Hu", ["Tr"] = "Tr"
    };
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".nes", ".unf", ".unif", ".sfc", ".smc", ".z64", ".n64", ".v64", ".rvz", ".gcm", ".ciso",
        ".gb", ".gbc", ".gba", ".nds", ".3ds", ".cia", ".cci", ".cxi", ".chd", ".iso", ".cue",
        ".bin", ".gdi", ".raw", ".cdi", ".pbp", ".cso", ".md", ".gen", ".smd", ".nsp", ".nsz",
        ".xci", ".xcz", ".zip", ".7z", ".rar", ".m3u", ".wad", ".wbfs", ".wud", ".wux", ".wua",
        ".rpx", ".pkg", ".adf", ".adz", ".hdf", ".ipf", ".lha", ".lzh", ".d64", ".d81", ".t64",
        ".tap", ".prg", ".crt", ".exe", ".com"
    };

    public ParsedRelease Parse(string releaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseName);
        if (releaseName.Length > 4096)
        {
            throw new ArgumentException("Release names may not exceed 4096 characters.", nameof(releaseName));
        }

        var title = releaseName.Trim();
        var extensionMatch = ExtensionPattern().Match(title);
        string? extension = null;
        if (extensionMatch.Success && Extensions.Contains(extensionMatch.Value))
        {
            extension = extensionMatch.Value.ToLowerInvariant();
            title = title[..extensionMatch.Index];
        }

        var regions = new List<string>();
        var languages = new List<string>();
        var statuses = new List<string>();
        var warnings = new List<string>();
        var serials = new List<string>();
        var identities = new List<SwitchTitleIdentity>();
        string? revision = null;
        int? discNumber = null;
        int? discCount = null;
        ContentKind? taggedKind = null;

        title = SerialPattern().Replace(title, match =>
        {
            serials.Add($"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}{match.Groups[3].Value}");
            return " ";
        });
        title = TitleIdPattern().Replace(title, match =>
        {
            if (!TryParseSwitchTitleId(match.Value, out var identity))
            {
                return match.Value;
            }
            identities.Add(identity!);
            return " ";
        });

        title = TagPattern().Replace(title, match =>
        {
            var tag = match.Groups[1].Success ? match.Groups[1].Value.Trim() : match.Groups[2].Value.Trim();
            if (tag.Length == 0) return " ";
            var tokens = tag.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && tokens.All(Regions.ContainsKey))
            {
                regions.AddRange(tokens.Select(token => Regions[token]));
                return " ";
            }
            if (tokens.Length > 0 && tokens.All(Languages.ContainsKey))
            {
                languages.AddRange(tokens.Select(token => Languages[token]));
                return " ";
            }
            var revisionMatch = RevisionPattern().Match(tag);
            if (revisionMatch.Success)
            {
                revision = revisionMatch.Groups[1].Value;
                return " ";
            }
            var discMatch = DiscPattern().Match(tag);
            if (discMatch.Success)
            {
                discNumber = int.Parse(discMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                discCount = discMatch.Groups[2].Success ? int.Parse(discMatch.Groups[2].Value, CultureInfo.InvariantCulture) : null;
                if (discNumber == 0 || discCount < discNumber) warnings.Add("InvalidDiscNumber");
                return " ";
            }
            var translation = TranslationPattern().Match(tag);
            if (translation.Success)
            {
                statuses.Add("Translation");
                statuses.Add(translation.Groups[1].Value == "+" ? "TranslationComplete" : "TranslationPartial");
                var language = translation.Groups[2].Value;
                languages.Add(Languages.GetValueOrDefault(language, language));
                return " ";
            }
            var numberedStatus = NumberedStatusPattern().Match(tag);
            var statusTag = numberedStatus.Success ? numberedStatus.Groups[1].Value : tag;
            var status = statusTag.ToLowerInvariant() switch
            {
                "!" => "VerifiedTag", "beta" => "Beta", "demo" => "Demo", "proto" or "prototype" => "Proto",
                "unl" or "unlicensed" => "Unl", "aftermarket" => "Aftermarket", "hack" => "Hack",
                "update" => "Update", "patch" => "Patch", "dlc" => "Dlc", _ => null
            };
            if (status is null) return match.Value;
            statuses.Add(status);
            var kind = status switch { "Update" or "Patch" => ContentKind.Update, "Dlc" => ContentKind.Dlc, _ => (ContentKind?)null };
            if (kind is not null)
            {
                if (taggedKind is not null && taggedKind != kind) warnings.Add("ConflictingContentKinds");
                taggedKind = kind;
            }
            return " ";
        });

        var uniqueIdentities = identities.Distinct().ToArray();
        if (uniqueIdentities.Length > 1) warnings.Add("MultipleTitleIds");
        var switchIdentity = uniqueIdentities.Length == 1 ? uniqueIdentities[0] : null;
        if (switchIdentity is not null && taggedKind is not null && switchIdentity.ContentKind != taggedKind)
        {
            warnings.Add("ConflictingContentKinds");
        }
        title = WhitespacePattern().Replace(title, " ").Trim(' ', '-', '_');
        if (title.Length == 0) warnings.Add("MissingTitle");
        return new ParsedRelease(releaseName, title, extension, regions.Distinct().ToArray(), languages.Distinct().ToArray(),
            statuses.Distinct().ToArray(), revision, discNumber, discCount, serials.Distinct().ToArray(), switchIdentity,
            switchIdentity?.ContentKind ?? taggedKind ?? ContentKind.Base, warnings.Distinct().ToArray());
    }

    public static bool TryParseSwitchTitleId(string value, out SwitchTitleIdentity? identity)
    {
        identity = null;
        if (value.Length != 16 || !ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)
            || id < 0x0100000000010000UL || id > 0x01FFFFFFFFFFFFFFUL) return false;
        var suffix = id & 0x1FFFUL;
        ContentKind kind;
        if (suffix == 0) kind = ContentKind.Base;
        else if (suffix == 0x800) kind = ContentKind.Update;
        else if (suffix > 0x1000) kind = ContentKind.Dlc;
        else return false;
        // DLC occupies the application block plus 0x1000; program sub-IDs are not DLC.
        var baseId = id & ~0x1FFFUL;
        identity = new SwitchTitleIdentity(id.ToString("X16"), baseId.ToString("X16"), kind);
        return true;
    }

    public static string NormalizeTitle(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        var article = TrailingArticlePattern().Match(normalized);
        if (article.Success) normalized = article.Groups[1].Value + " " + normalized[..article.Index];
        return new string(normalized.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }

    [GeneratedRegex(@"\.[a-zA-Z0-9]{2,5}$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ExtensionPattern();
    [GeneratedRegex(@"(?<![A-Za-z0-9])(SLUS|SCUS|SCES|SLES|SCPS|SLPS|SLPM|SIPS|PAPX|PCPX|PBPX|SLED|SCED|SCCS|SLKA|SCKA|ULUS|ULES|ULJM|ULJS|UCUS|UCES|UCJS|UCJM|NPUB|NPEB|NPJB|NPUH|NPEH|BLUS|BLES|BCUS|BCES|BLJS|BCJS|BCAS|BLAS|CUSA|PPSA)[\s_.-]*(\d{3})[.]?(\d{2})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex SerialPattern();
    [GeneratedRegex(@"(?<![a-fA-F0-9])01[a-fA-F0-9]{14}(?![a-fA-F0-9])", RegexOptions.CultureInvariant, 100)]
    private static partial Regex TitleIdPattern();
    [GeneratedRegex(@"\(([^()]*)\)|\[([^\[\]]*)\]", RegexOptions.CultureInvariant, 100)]
    private static partial Regex TagPattern();
    [GeneratedRegex(@"^(?:Rev(?:ision)?\s+|v)([0-9]+(?:\.[0-9]+)*[a-z]?|[A-Z])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex RevisionPattern();
    [GeneratedRegex(@"^(?:Disc|Disk|CD)\s*(\d{1,3})(?:\s*(?:of|/)\s*(\d{1,3}))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex DiscPattern();
    [GeneratedRegex(@"^T([+-])([a-z]{2,7})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex TranslationPattern();
    [GeneratedRegex(@"^(Beta|Demo|Proto|Prototype)\s+\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex NumberedStatusPattern();
    [GeneratedRegex(@",\s*(The|A|An|Der|Die|Das|Le|La|Les)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex TrailingArticlePattern();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 100)]
    private static partial Regex WhitespacePattern();
}
