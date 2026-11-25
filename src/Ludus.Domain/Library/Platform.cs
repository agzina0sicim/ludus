using Ludus.Domain.Common;

namespace Ludus.Domain.Library;

public sealed class Platform : Entity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string AccentColor { get; set; } = "#38bdf8";
    public bool Monitored { get; set; } = true;
    public bool SupportsFolderGames { get; set; }
    public bool ExtractArchives { get; set; }
    public MetadataProviderKind PrimaryMetadataProvider { get; set; } = MetadataProviderKind.Igdb;
    public bool MetadataFallbackEnabled { get; set; } = true;
    public int? IgdbPlatformId { get; set; }
    public int? ScreenScraperSystemId { get; set; }
    public List<string> AllowedExtensions { get; set; } = [];
    public List<int> TorznabCategories { get; set; } = [1000];
}
