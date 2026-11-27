using Ludus.Domain.Common;
using Ludus.Domain.Profiles;

namespace Ludus.Domain.Library;

public sealed class Game : Entity
{
    public required string Title { get; set; }
    public required string SortTitle { get; set; }
    public string? Description { get; set; }
    public int? ReleaseYear { get; set; }
    public DateTime? ReleaseDateUtc { get; set; }
    public ReleaseDatePrecision? ReleaseDatePrecision { get; set; }
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public List<string> Genres { get; set; } = [];
    public List<string> Serials { get; set; } = [];
    public string? BaseTitleId { get; set; }
    public string? CoverRelativePath { get; set; }
    public string? ManagedFolderRelativePath { get; set; }
    public bool Monitored { get; set; } = true;
    public GameStatus Status { get; set; } = GameStatus.Missing;
    public long PlatformId { get; set; }
    public Platform Platform { get; set; } = null!;
    public long RootFolderId { get; set; }
    public RootFolder RootFolder { get; set; } = null!;
    public long QualityProfileId { get; set; }
    public QualityProfile QualityProfile { get; set; } = null!;
    public DateTime? MetadataUpdatedAtUtc { get; set; }
    public ICollection<GameFile> Files { get; set; } = [];
    public ICollection<GameMetadataReference> MetadataReferences { get; set; } = [];
    public ICollection<GameMetadataTranslation> MetadataTranslations { get; set; } = [];
}

public sealed class GameMetadataReference : Entity
{
    public long GameId { get; set; }
    public Game Game { get; set; } = null!;
    public MetadataProviderKind Provider { get; set; }
    public required string ExternalId { get; set; }
}

public sealed class GameMetadataTranslation : Entity
{
    public long GameId { get; set; }
    public Game Game { get; set; } = null!;
    public MetadataProviderKind Provider { get; set; }
    public required string Language { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? TitleLanguage { get; set; }
    public string? DescriptionLanguage { get; set; }
}
