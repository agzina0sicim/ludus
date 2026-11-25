using System.Globalization;
using System.Text.RegularExpressions;
using Ludus.Application.Releases;
using Ludus.Domain.Common;
using Ludus.Domain.Profiles;

namespace Ludus.Application.Profiles;

public sealed class CustomFormatEvaluator
{
    public bool Matches(CustomFormat format, ParsedRelease release)
    {
        if (format.Rules.Count == 0 || format.Rules.Count > 100)
            throw new ArgumentException("A custom format must contain between 1 and 100 rules.");
        var matches = format.Rules.Select(rule => (Rule: rule, Matches: MatchesRule(rule, release))).ToArray();
        if (matches.Any(x => x.Rule.Required && !x.Matches)) return false;
        return format.MatchMode switch
        {
            FormatMatchMode.All => matches.All(x => x.Matches),
            FormatMatchMode.Any => matches.Any(x => x.Matches),
            _ => throw new ArgumentException("Invalid custom format match mode.")
        };
    }

    private static bool MatchesRule(CustomFormatRule rule, ParsedRelease release)
    {
        if (string.IsNullOrWhiteSpace(rule.Value) || rule.Value.Length > 2048)
            throw new ArgumentException("Invalid custom format rule value.");
        var values = Values(rule.Field, release);
        Regex? regex = rule.Operator == FormatOperator.Regex
            ? new Regex(rule.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)) : null;
        if (!Enum.IsDefined(rule.Operator)) throw new ArgumentException("Invalid custom format operator.");
        if (rule.Operator == FormatOperator.GreaterThanOrEqual && !TryVersion(rule.Value, out _))
            throw new ArgumentException("Invalid numeric revision rule.");
        var matched = values.Any(value => rule.Operator switch
        {
            FormatOperator.Equals => string.Equals(value, rule.Value, StringComparison.OrdinalIgnoreCase),
            FormatOperator.Contains => value.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
            FormatOperator.Regex => regex!.IsMatch(value),
            FormatOperator.GreaterThanOrEqual => CompareVersions(value, rule.Value),
            _ => false
        });
        return rule.Negate ? !matched : matched;
    }

    private static IReadOnlyList<string> Values(FormatField field, ParsedRelease release) => field switch
    {
        FormatField.Region => release.Regions,
        FormatField.Language => release.Languages,
        FormatField.Status => release.StatusTags,
        FormatField.Serial => release.Serials,
        FormatField.ReleaseTitle => [release.OriginalTitle],
        FormatField.Extension => release.Extension is null ? [] : [release.Extension],
        FormatField.Revision => release.Revision is null ? [] : [release.Revision],
        FormatField.TitleId => release.SwitchIdentity is null ? [] : [release.SwitchIdentity.TitleId],
        FormatField.ContentKind => [release.ContentKind.ToString()],
        _ => throw new ArgumentException("Invalid custom format field.")
    };

    private static bool CompareVersions(string actual, string minimum)
    {
        if (!TryVersion(actual, out var left) || !TryVersion(minimum, out var right)) return false;
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var comparison = (i < left.Length ? left[i] : 0).CompareTo(i < right.Length ? right[i] : 0);
            if (comparison != 0) return comparison > 0;
        }
        return true;
    }

    private static bool TryVersion(string value, out int[] numbers)
    {
        var components = value.TrimStart('v', 'V').Split('.');
        numbers = new int[components.Length];
        if (components.Length > 8) return false;
        for (var i = 0; i < components.Length; i++)
            if (!int.TryParse(components[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        return true;
    }
}
