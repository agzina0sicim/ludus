using Ludus.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludus.Infrastructure.Persistence.Configurations;

public sealed class ProfileConfiguration : IEntityTypeConfiguration<QualityProfile>, IEntityTypeConfiguration<CustomFormat>,
    IEntityTypeConfiguration<CustomFormatRule>, IEntityTypeConfiguration<QualityProfileFormat>
{
    public void Configure(EntityTypeBuilder<QualityProfile> entity)
    {
        entity.HasIndex(x => new { x.PlatformId, x.Name }).IsUnique();
        entity.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId).OnDelete(DeleteBehavior.Restrict);
        entity.JsonList(x => x.AllowedRegions);
        entity.JsonList(x => x.ExcludedRegions);
        entity.Check("Cutoff", "\"UpgradeUntilScore\" >= \"MinimumScore\"");
    }

    public void Configure(EntityTypeBuilder<CustomFormat> entity) => entity.HasIndex(x => x.Name).IsUnique();

    public void Configure(EntityTypeBuilder<CustomFormatRule> entity)
    {
        entity.HasOne(x => x.CustomFormat).WithMany(x => x.Rules).HasForeignKey(x => x.CustomFormatId).OnDelete(DeleteBehavior.Cascade);
        entity.Property(x => x.Value).HasMaxLength(2048);
    }

    public void Configure(EntityTypeBuilder<QualityProfileFormat> entity)
    {
        entity.HasKey(x => new { x.QualityProfileId, x.CustomFormatId });
        entity.HasOne(x => x.QualityProfile).WithMany(x => x.Formats).HasForeignKey(x => x.QualityProfileId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.CustomFormat).WithMany().HasForeignKey(x => x.CustomFormatId).OnDelete(DeleteBehavior.Cascade);
    }
}
