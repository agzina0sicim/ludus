using Ludus.Domain.Common;

namespace Ludus.Domain.Integrations;

public sealed class MetadataProviderSettings : Entity
{
    public MetadataProviderKind Provider { get; set; }
    public bool Enabled { get; set; }
    public ProtectedSecret? ClientId { get; set; }
    public ProtectedSecret? ClientSecret { get; set; }
    public ProtectedSecret? Username { get; set; }
    public ProtectedSecret? Password { get; set; }
    public ProtectedSecret? AccessToken { get; set; }
    public DateTime? AccessTokenExpiresAtUtc { get; set; }
    public int CacheDurationHours { get; set; } = 168;
}

public sealed class MetadataCacheEntry : Entity
{
    public MetadataProviderKind Provider { get; set; }
    public required string CacheKey { get; set; }
    public required string PayloadJson { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
