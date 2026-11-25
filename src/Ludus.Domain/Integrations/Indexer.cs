using Ludus.Domain.Common;

namespace Ludus.Domain.Integrations;

public sealed class Indexer : Entity
{
    public required string Name { get; set; }
    public required string BaseUrl { get; set; }
    public ProtectedSecret? ApiKey { get; set; }
    public IndexerKind Kind { get; set; }
    public DownloadProtocol Protocol { get; set; }
    public bool EnableRss { get; set; } = true;
    public bool EnableAutomaticSearch { get; set; } = true;
    public bool EnableInteractiveSearch { get; set; } = true;
    public int Priority { get; set; } = 25;
    public int MinimumSeeders { get; set; } = 1;
    public List<int> Categories { get; set; } = [1000];
    public string? CapabilitiesXml { get; set; }
    public DateTime? CapabilitiesUpdatedAtUtc { get; set; }
    public DateTime? LastRssSyncAtUtc { get; set; }
    public string? LastRssGuid { get; set; }
}
