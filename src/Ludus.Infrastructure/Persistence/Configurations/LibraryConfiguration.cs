using Ludus.Domain.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludus.Infrastructure.Persistence.Configurations;

public sealed class LibraryConfiguration : IEntityTypeConfiguration<Platform>, IEntityTypeConfiguration<RootFolder>,
    IEntityTypeConfiguration<Game>, IEntityTypeConfiguration<GameFile>, IEntityTypeConfiguration<GameMetadataReference>
{
    public void Configure(EntityTypeBuilder<Platform> entity)
    {
        entity.Property(x => x.Name).HasMaxLength(128);
        entity.Property(x => x.Slug).HasMaxLength(64);
        entity.HasIndex(x => x.Slug).IsUnique();
        entity.JsonList(x => x.AllowedExtensions);
        entity.JsonList(x => x.TorznabCategories);
    }

    public void Configure(EntityTypeBuilder<RootFolder> entity)
    {
        entity.Property(x => x.Name).HasMaxLength(128);
        entity.Property(x => x.Path).HasMaxLength(4096);
        entity.HasIndex(x => x.Path).IsUnique();
        entity.Check("Path", "length(trim(\"Path\")) > 0");
    }

    public void Configure(EntityTypeBuilder<Game> entity)
    {
        entity.Property(x => x.Title).HasMaxLength(512);
        entity.Property(x => x.SortTitle).HasMaxLength(512);
        entity.HasIndex(x => x.SortTitle);
        entity.HasIndex(x => new { x.PlatformId, x.Monitored, x.Status });
        entity.HasIndex(x => new { x.PlatformId, x.BaseTitleId }).IsUnique().HasFilter("\"BaseTitleId\" IS NOT NULL");
        entity.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.RootFolder).WithMany().HasForeignKey(x => x.RootFolderId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.QualityProfile).WithMany().HasForeignKey(x => x.QualityProfileId).OnDelete(DeleteBehavior.Restrict);
        entity.JsonList(x => x.Genres);
        entity.JsonList(x => x.Serials);
        entity.Hash(x => x.BaseTitleId, 16);
        entity.Check("Year", "\"ReleaseYear\" IS NULL OR \"ReleaseYear\" BETWEEN 1900 AND 9999");
        entity.Check("Title", "length(trim(\"Title\")) > 0");
    }

    public void Configure(EntityTypeBuilder<GameMetadataReference> entity)
    {
        entity.Property(x => x.ExternalId).HasMaxLength(128);
        entity.HasIndex(x => new { x.GameId, x.Provider }).IsUnique();
        entity.HasIndex(x => new { x.Provider, x.ExternalId });
        entity.HasOne(x => x.Game).WithMany(x => x.MetadataReferences).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<GameFile> entity)
    {
        entity.HasOne(x => x.Game).WithMany(x => x.Files).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.DatRom).WithMany().HasForeignKey(x => x.DatRomId).OnDelete(DeleteBehavior.SetNull);
        entity.HasIndex(x => new { x.GameId, x.RelativePath }).IsUnique();
        entity.HasIndex(x => new { x.GameId, x.ContentKind, x.Missing });
        entity.HasIndex(x => x.FileGroupId);
        entity.HasIndex(x => new { x.Sha1, x.Size });
        entity.HasIndex(x => new { x.Crc32, x.Size });
        entity.Property(x => x.RelativePath).HasMaxLength(4096);
        entity.Hash(x => x.Crc32, 8);
        entity.Hash(x => x.Md5, 32);
        entity.Hash(x => x.Sha1, 40);
        entity.Hash(x => x.TitleId, 16);
        entity.JsonList(x => x.Regions);
        entity.JsonList(x => x.Languages);
        entity.Check("Size", "\"Size\" >= 0");
        entity.Check("Disc", "\"DiscNumber\" IS NULL OR \"DiscNumber\" > 0");
        entity.Check("RelativePath", "length(\"RelativePath\") > 0 AND substr(\"RelativePath\", 1, 1) <> '/' AND instr(\"RelativePath\", ':') = 0 AND instr(\"RelativePath\", char(92)) = 0 AND instr('/' || \"RelativePath\" || '/', '/../') = 0 AND instr('/' || \"RelativePath\" || '/', '/./') = 0");
    }
}
