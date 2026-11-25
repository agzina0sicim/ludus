using Ludus.Domain.Configuration;
using Ludus.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludus.Infrastructure.Persistence.Configurations;

public sealed class SystemConfiguration : IEntityTypeConfiguration<AppSettings>, IEntityTypeConfiguration<SecuritySettings>,
    IEntityTypeConfiguration<ScheduledTask>, IEntityTypeConfiguration<TaskRun>, IEntityTypeConfiguration<HealthIssue>,
    IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<AppSettings> entity)
    {
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Check("Singleton", "\"Id\" = 1");
        entity.Check("Intervals", "\"RssIntervalMinutes\" >= 1 AND \"DownloadRefreshIntervalSeconds\" >= 5 AND \"LibraryScanIntervalHours\" >= 1 AND \"HealthCheckIntervalMinutes\" >= 1 AND \"BackupIntervalHours\" >= 1");
        entity.Check("Retention", "\"BackupRetentionCount\" >= 1 AND \"RecyclingRetentionDays\" >= 1");
        entity.Check("MediaLifetime", "\"MediaLinkLifetimeSeconds\" BETWEEN 30 AND 3600");
        entity.Check("ArchiveLimits", "\"MaximumArchiveEntries\" > 0 AND \"MaximumExtractedBytes\" > 0");
        entity.Check("Language", "\"Language\" IN ('de', 'en')");
    }

    public void Configure(EntityTypeBuilder<SecuritySettings> entity)
    {
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Check("Singleton", "\"Id\" = 1");
        entity.Check("FormsCredentials", "\"FormsAuthenticationEnabled\" = 0 OR (\"Username\" IS NOT NULL AND length(trim(\"Username\")) > 0 AND \"PasswordHash\" IS NOT NULL AND length(\"PasswordHash\") > 0)");
        entity.Check("ForwardLimit", "\"ForwardLimit\" BETWEEN 1 AND 10");
        entity.JsonList(x => x.TrustedProxyNetworks);
    }

    public void Configure(EntityTypeBuilder<ScheduledTask> entity) => entity.HasIndex(x => x.Kind).IsUnique();

    public void Configure(EntityTypeBuilder<TaskRun> entity)
    {
        entity.HasOne(x => x.ScheduledTask).WithMany().HasForeignKey(x => x.ScheduledTaskId).OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(x => new { x.ScheduledTaskId, x.StartedAtUtc });
    }

    public void Configure(EntityTypeBuilder<HealthIssue> entity) => entity.HasIndex(x => new { x.Code, x.ResourceKey }).IsUnique();

    public void Configure(EntityTypeBuilder<BackupRecord> entity)
    {
        entity.HasIndex(x => x.RelativePath).IsUnique();
        entity.Hash(x => x.Sha256, 64);
        entity.Check("Size", "\"Size\" >= 0");
    }
}
