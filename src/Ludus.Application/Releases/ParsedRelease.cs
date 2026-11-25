using Ludus.Domain.Common;

namespace Ludus.Application.Releases;

public sealed record SwitchTitleIdentity(string TitleId, string BaseTitleId, ContentKind ContentKind);

public sealed record ParsedRelease(
    string OriginalTitle,
    string Title,
    string? Extension,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> StatusTags,
    string? Revision,
    int? DiscNumber,
    int? DiscCount,
    IReadOnlyList<string> Serials,
    SwitchTitleIdentity? SwitchIdentity,
    ContentKind ContentKind,
    IReadOnlyList<string> Warnings);
