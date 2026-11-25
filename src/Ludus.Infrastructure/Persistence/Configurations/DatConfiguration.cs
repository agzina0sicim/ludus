using Ludus.Domain.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludus.Infrastructure.Persistence.Configurations;

public sealed class DatConfiguration : IEntityTypeConfiguration<DatFile>, IEntityTypeConfiguration<DatGame>, IEntityTypeConfiguration<DatRom>
{
    public void Configure(EntityTypeBuilder<DatFile> entity)
    {
        entity.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.PlatformId, x.Sha256 }).IsUnique();
        entity.Hash(x => x.Sha256, 64);
    }

    public void Configure(EntityTypeBuilder<DatGame> entity)
    {
        entity.HasOne(x => x.DatFile).WithMany(x => x.Games).HasForeignKey(x => x.DatFileId).OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(x => new { x.DatFileId, x.Name }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<DatRom> entity)
    {
        entity.HasOne(x => x.DatGame).WithMany(x => x.Roms).HasForeignKey(x => x.DatGameId).OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(x => new { x.DatGameId, x.Name }).IsUnique();
        entity.HasIndex(x => new { x.Crc32, x.Size });
        entity.HasIndex(x => new { x.Md5, x.Size });
        entity.HasIndex(x => new { x.Sha1, x.Size });
        entity.Hash(x => x.Crc32, 8);
        entity.Hash(x => x.Md5, 32);
        entity.Hash(x => x.Sha1, 40);
        entity.Property(x => x.Crc32).UseCollation("NOCASE");
        entity.Property(x => x.Md5).UseCollation("NOCASE");
        entity.Property(x => x.Sha1).UseCollation("NOCASE");
        entity.Check("Size", "\"Size\" IS NULL OR \"Size\" >= 0");
    }
}
