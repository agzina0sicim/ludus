using Ludus.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludus.Infrastructure.Persistence.Configurations;

public sealed class IntegrationConfiguration : IEntityTypeConfiguration<Indexer>, IEntityTypeConfiguration<DownloadClient>,
    IEntityTypeConfiguration<RemotePathMapping>, IEntityTypeConfiguration<MetadataProviderSettings>,
    IEntityTypeConfiguration<MetadataCacheEntry>, IEntityTypeConfiguration<Webhook>
{
    public void Configure(EntityTypeBuilder<Indexer> entity)
    {
        entity.HasIndex(x => x.Name).IsUnique();
        entity.JsonList(x => x.Categories);
        entity.Check("Priority", "\"Priority\" BETWEEN 1 AND 100");
        entity.Check("Seeders", "\"MinimumSeeders\" >= 0");
        entity.Check("BaseUrl", "instr(\"BaseUrl\", '?') = 0 AND instr(\"BaseUrl\", '@') = 0 AND instr(\"BaseUrl\", '#') = 0 AND (\"BaseUrl\" LIKE 'http://%' OR \"BaseUrl\" LIKE 'https://%')");
    }

    public void Configure(EntityTypeBuilder<DownloadClient> entity)
    {
        entity.HasIndex(x => x.Name).IsUnique();
        entity.Check("Category", "\"Category\" = 'ludus'");
        entity.Check("Priority", "\"Priority\" BETWEEN 1 AND 100");
        entity.Check("BaseUrl", "instr(\"BaseUrl\", '?') = 0 AND instr(\"BaseUrl\", '@') = 0 AND instr(\"BaseUrl\", '#') = 0 AND (\"BaseUrl\" LIKE 'http://%' OR \"BaseUrl\" LIKE 'https://%')");
    }

    public void Configure(EntityTypeBuilder<RemotePathMapping> entity)
    {
        entity.Property(x => x.Host).UseCollation("NOCASE");
        entity.HasIndex(x => new { x.Host, x.RemotePath }).IsUnique();
        entity.Check("Paths", "length(\"Host\") > 0 AND length(\"RemotePath\") > 0 AND length(\"LocalPath\") > 0");
    }

    public void Configure(EntityTypeBuilder<MetadataProviderSettings> entity)
    {
        entity.HasIndex(x => x.Provider).IsUnique();
        entity.Check("CacheHours", "\"CacheDurationHours\" > 0");
    }

    public void Configure(EntityTypeBuilder<MetadataCacheEntry> entity)
    {
        entity.HasIndex(x => new { x.Provider, x.CacheKey }).IsUnique();
        entity.HasIndex(x => x.ExpiresAtUtc);
        entity.Check("Json", "json_valid(\"PayloadJson\")");
    }

    public void Configure(EntityTypeBuilder<Webhook> entity)
    {
        entity.HasIndex(x => x.Name).IsUnique();
        entity.JsonList(x => x.Events);
    }
}
