using System.Text.RegularExpressions;

namespace Ludus.Infrastructure.Imports;

public sealed partial class DescriptorFiles
{
    public async Task<IReadOnlyList<string>> ReferencesAsync(string path, string contentRoot, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new ImportException("DescriptorTooLarge", true);
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        var references = new List<string>();
        if (Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var line in lines)
            {
                var match = CueFilePattern().Match(line);
                if (match.Success) references.Add(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
                else if (line.TrimStart().StartsWith("FILE ", StringComparison.OrdinalIgnoreCase)) throw new ImportException("InvalidCue", true);
            }
        }
        else
        {
            if (lines.Length == 0 || !int.TryParse(lines[0], out var count) || count < 1 || count != lines.Skip(1).Count(x => !string.IsNullOrWhiteSpace(x)))
                throw new ImportException("InvalidGdi", true);
            foreach (var line in lines.Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var match = GdiFilePattern().Match(line);
                if (!match.Success) throw new ImportException("InvalidGdi", true);
                references.Add(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
            }
        }
        if (references.Count == 0) throw new ImportException("EmptyDescriptor", true);
        return references.Select(reference =>
        {
            var resolved = SafePaths.Within(Path.GetDirectoryName(path)!, reference);
            if (!SafePaths.IsWithin(contentRoot, resolved) || !File.Exists(resolved)) throw new ImportException("MissingDiscTrack", true);
            return resolved;
        }).Distinct().ToArray();
    }

    [GeneratedRegex("^\\s*FILE\\s+(?:\"([^\"]+)\"|([^\\s]+))\\s+\\S+\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex CueFilePattern();
    [GeneratedRegex("^\\s*\\d+\\s+\\d+\\s+\\d+\\s+\\d+\\s+(?:\"([^\"]+)\"|(\\S+))\\s+\\d+\\s*$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex GdiFilePattern();
}
