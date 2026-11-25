using Ludus.Domain.Activity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludus.Infrastructure.Persistence.Configurations;

public sealed class ActivityConfiguration : IEntityTypeConfiguration<TrackedDownload>, IEntityTypeConfiguration<ReleaseBlock>,
    IEntityTypeConfiguration<HistoryEntry>, IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<TrackedDownload> entity)
    {
        entity.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.DownloadClient).WithMany().HasForeignKey(x => x.DownloadClientId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Indexer).WithMany().HasForeignKey(x => x.IndexerId).OnDelete(DeleteBehavior.SetNull);
        entity.HasIndex(x => new { x.DownloadClientId, x.DownloadId }).IsUnique();
        entity.HasIndex(x => new { x.GameId, x.ReleaseFingerprint });
        entity.HasIndex(x => x.Status);
        entity.HasIndex(x => x.SubmissionToken).IsUnique();
        entity.Check("Progress", "\"Size\" >= 0 AND \"DownloadedBytes\" >= 0 AND \"SpeedBytesPerSecond\" >= 0 AND (\"EtaSeconds\" IS NULL OR \"EtaSeconds\" >= 0) AND \"ImportAttempts\" >= 0");
    }

    public void Configure(EntityTypeBuilder<WebhookDelivery> entity)
    {
        entity.HasOne(x => x.Webhook).WithMany().HasForeignKey(x => x.WebhookId).OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(x => new { x.CompletedAtUtc, x.NextAttemptAtUtc });
        entity.Check("Attempts", "\"Attempts\" >= 0");
    }

    public void Configure(EntityTypeBuilder<ReleaseBlock> entity)
    {
        entity.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Indexer).WithMany().HasForeignKey(x => x.IndexerId).OnDelete(DeleteBehavior.SetNull);
        entity.HasIndex(x => new { x.GameId, x.ReleaseFingerprint }).IsUnique();
        entity.HasIndex(x => x.ExpiresAtUtc);
        entity.Check("Expiry", "(\"Kind\" = 'Permanent' AND \"ExpiresAtUtc\" IS NULL) OR (\"Kind\" = 'Cooldown' AND \"ExpiresAtUtc\" IS NOT NULL AND \"ExpiresAtUtc\" > \"CreatedAtUtc\")");
    }

    public void Configure(EntityTypeBuilder<HistoryEntry> entity)
    {
        entity.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.Download).WithMany().HasForeignKey(x => x.DownloadId).OnDelete(DeleteBehavior.SetNull);
        entity.HasIndex(x => new { x.GameId, x.CreatedAtUtc });
        entity.HasIndex(x => x.CreatedAtUtc);
    }
}
