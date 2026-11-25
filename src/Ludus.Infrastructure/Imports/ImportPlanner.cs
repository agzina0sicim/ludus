using System.Globalization;
using System.Text.RegularExpressions;
using Ludus.Application.Releases;
using Ludus.Domain.Activity;
using Ludus.Domain.Common;
using Ludus.Domain.Configuration;
using Ludus.Domain.Library;

namespace Ludus.Infrastructure.Imports;

public sealed record PlannedSource(string Source, string RelativeTarget, FileRole Role, ParsedRelease Parsed, Guid GroupId);

public sealed partial class ImportPlanner(ReleaseParser parser, DescriptorFiles descriptors)
{
    public async Task<IReadOnlyList<PlannedSource>> PlanAsync(TrackedDownload download, string contentPath, AppSettings settings, CancellationToken cancellationToken)
    {
        var game = download.Game;
        var directory = Directory.Exists(contentPath) ? contentPath : Path.GetDirectoryName(contentPath)!;
        var files = Directory.Exists(contentPath) ? SafePaths.EnumerateFiles(contentPath, settings.MaximumArchiveEntries) : [contentPath];
        var release = parser.Parse(download.ReleaseTitle);
        var folderGame = game.Platform.SupportsFolderGames && Directory.Exists(contentPath)
            && (files.Any(x => ("/" + Path.GetRelativePath(contentPath, x).Replace('\\', '/')).EndsWith("/PS3_GAME/PARAM.SFO", StringComparison.OrdinalIgnoreCase))
                || files.Any(x => Path.GetExtension(x).Equals(".rpx", StringComparison.OrdinalIgnoreCase))
                || game.Platform.Slug == "dos" && files.Any(x => Path.GetExtension(x).ToLowerInvariant() is ".exe" or ".com"));
        var selected = files.Where(x => game.Platform.AllowedExtensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase)
            && !Path.GetExtension(x).Equals(".m3u", StringComparison.OrdinalIgnoreCase)).ToHashSet(SafePaths.Comparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var descriptorInfo = new Dictionary<string, (ParsedRelease Parsed, Guid Group)>();
        foreach (var descriptor in selected.Where(x => Path.GetExtension(x).ToLowerInvariant() is ".cue" or ".gdi").ToArray())
        {
            var parsed = Merge(parser.Parse(Path.GetFileName(descriptor)), release);
            var group = Guid.NewGuid();
            descriptorInfo[descriptor] = (parsed, group);
            foreach (var track in await descriptors.ReferencesAsync(descriptor, directory, cancellationToken))
            {
                selected.Add(track);
                if (descriptorInfo.TryGetValue(track, out var previous) && previous.Group != group) throw new ImportException("SharedDiscTrack", true);
                descriptorInfo[track] = (parsed, group);
            }
        }
        if (folderGame) selected = files.ToHashSet();
        if (selected.Count == 0) throw new ImportException("NoPlatformFiles", true);
        var result = new List<PlannedSource>();
        var groups = new Dictionary<string, Guid>();
        foreach (var file in selected.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafePaths.RejectLinks(file);
            var hasDescriptor = descriptorInfo.TryGetValue(file, out var disc);
            var parsed = folderGame ? release with { Extension = null }
                : hasDescriptor ? disc.Parsed : Merge(parser.Parse(Path.GetFileName(file)), release);
            if (parsed.Warnings.Count > 0 || (parsed.SwitchIdentity is not null && game.BaseTitleId is not null
                && !parsed.SwitchIdentity.BaseTitleId.Equals(game.BaseTitleId, StringComparison.OrdinalIgnoreCase)))
                throw new ImportException("ContentIdentityMismatch", true);
            var groupKey = $"{parsed.ContentKind}:{parsed.SwitchIdentity?.TitleId}:{parsed.DiscNumber}";
            if (!groups.TryGetValue(groupKey, out var group)) groups[groupKey] = group = Guid.NewGuid();
            if (descriptorInfo.ContainsKey(file)) group = disc.Group;
            var extension = Path.GetExtension(file).ToLowerInvariant();
            var named = Render(settings.NamingSchema, game, parsed, extension.TrimStart('.'));
            var folder = Path.GetDirectoryName(named)?.Replace('\\', '/') ?? "";
            if (parsed.ContentKind != ContentKind.Base)
            {
                var identity = parsed.SwitchIdentity?.TitleId ?? SafePaths.Component(release.Title);
                folder = Join(folder, parsed.ContentKind == ContentKind.Update ? "Updates" : "DLC", identity);
            }
            var relative = folderGame ? Join(folder, SafePaths.Component(game.Title), Path.GetRelativePath(directory, file).Replace('\\', '/'))
                : selected.Count > 1 || !settings.RenameFiles ? Join(folder, Path.GetRelativePath(directory, file).Replace('\\', '/'))
                : Join(folder, Path.GetFileName(named));
            var role = extension is ".cue" or ".gdi" ? FileRole.Descriptor : descriptorInfo.ContainsKey(file) ? FileRole.Track : FileRole.Rom;
            if (folderGame) role = result.Count == 0 ? FileRole.Rom : FileRole.Support;
            SafePaths.Within(game.RootFolder.Path, relative);
            result.Add(new PlannedSource(file, relative, role, parsed, group));
        }
        if (result.Select(x => x.RelativeTarget).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count) throw new ImportException("NamingCollision", false);
        return result;
    }

    public static string Render(string template, Game game, ParsedRelease release, string extension)
    {
        var relative = template.StartsWith("{RootFolder}/", StringComparison.Ordinal) ? template[13..] : template;
        var values = new Dictionary<string, string>
        {
            ["Game Title"] = SafePaths.Component(game.Title), ["Year"] = game.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? "Unknown",
            ["Region"] = SafePaths.Component(release.Regions.Count == 0 ? "Unknown" : string.Join(", ", release.Regions)), ["ext"] = extension
        };
        return TokenPattern().Replace(relative, match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : throw new ImportException("UnknownNamingToken", false));
    }

    private static ParsedRelease Merge(ParsedRelease file, ParsedRelease release) => file with
    {
        Regions = file.Regions.Count > 0 ? file.Regions : release.Regions,
        Languages = file.Languages.Count > 0 ? file.Languages : release.Languages,
        DiscNumber = file.DiscNumber ?? release.DiscNumber,
        Revision = file.Revision ?? release.Revision,
        StatusTags = file.StatusTags.Concat(release.StatusTags).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        Warnings = file.Warnings.Concat(release.Warnings).Distinct().ToArray(),
        ContentKind = file.SwitchIdentity?.ContentKind ?? (file.StatusTags.Any(x => x is "Update" or "Patch" or "Dlc") ? file.ContentKind : release.ContentKind),
        SwitchIdentity = file.SwitchIdentity ?? release.SwitchIdentity
    };
    private static string Join(params string[] values) => string.Join('/', values.Where(x => x.Length > 0));
    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant, 100)]
    private static partial Regex TokenPattern();
}
