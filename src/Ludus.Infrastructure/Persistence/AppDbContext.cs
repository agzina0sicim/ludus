using Ludus.Domain.Activity;
using Ludus.Domain.Common;
using Ludus.Domain.Configuration;
using Ludus.Domain.Integrations;
using Ludus.Domain.Library;
using Ludus.Domain.Operations;
using Ludus.Domain.Profiles;
using Ludus.Domain.Verification;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<RootFolder> RootFolders => Set<RootFolder>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameFile> GameFiles => Set<GameFile>();
    public DbSet<GameMetadataReference> GameMetadataReferences => Set<GameMetadataReference>();
    public DbSet<DatFile> DatFiles => Set<DatFile>();
    public DbSet<DatGame> DatGames => Set<DatGame>();
    public DbSet<DatRom> DatRoms => Set<DatRom>();
    public DbSet<QualityProfile> QualityProfiles => Set<QualityProfile>();
    public DbSet<CustomFormat> CustomFormats => Set<CustomFormat>();
    public DbSet<CustomFormatRule> CustomFormatRules => Set<CustomFormatRule>();
    public DbSet<QualityProfileFormat> QualityProfileFormats => Set<QualityProfileFormat>();
    public DbSet<Indexer> Indexers => Set<Indexer>();
    public DbSet<DownloadClient> DownloadClients => Set<DownloadClient>();
    public DbSet<RemotePathMapping> RemotePathMappings => Set<RemotePathMapping>();
    public DbSet<MetadataProviderSettings> MetadataProviders => Set<MetadataProviderSettings>();
    public DbSet<MetadataCacheEntry> MetadataCache => Set<MetadataCacheEntry>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<TrackedDownload> Downloads => Set<TrackedDownload>();
    public DbSet<ReleaseBlock> ReleaseBlocks => Set<ReleaseBlock>();
    public DbSet<HistoryEntry> History => Set<HistoryEntry>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<SecuritySettings> SecuritySettings => Set<SecuritySettings>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<TaskRun> TaskRuns => Set<TaskRun>();
    public DbSet<HealthIssue> HealthIssues => Set<HealthIssue>();
    public DbSet<BackupRecord> Backups => Set<BackupRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<ProtectedSecret>().HaveConversion<SecretConverter>();
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToArray())
        {
            var entity = modelBuilder.Entity(entityType.ClrType);
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                entity.HasKey(nameof(Entity.Id));
                entity.Property(nameof(Entity.Version)).IsConcurrencyToken();
                entity.Property(nameof(Entity.CreatedAtUtc)).IsRequired();
                entity.Property(nameof(Entity.UpdatedAtUtc)).IsRequired();
            }

            foreach (var property in entityType.GetProperties().ToArray())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!type.IsEnum)
                {
                    continue;
                }

                entity.Property(property.Name).HasConversion<string>().HasMaxLength(64);
                var values = string.Join(", ", Enum.GetNames(type).Select(name => $"'{name}'"));
                entity.ToTable(table => table.HasCheckConstraint(
                    $"CK_{entityType.GetTableName()}_{property.Name}", $"\"{property.Name}\" IN ({values})"));
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareChanges()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
            }
            else
            {
                entry.Property(entity => entity.CreatedAtUtc).IsModified = false;
            }

            entry.Entity.UpdatedAtUtc = now;
            entry.Entity.Version = Guid.NewGuid();

            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is DateTime dateTime && dateTime.Kind != DateTimeKind.Utc)
                {
                    throw new InvalidOperationException($"{entry.Metadata.ClrType.Name}.{property.Metadata.Name} must be UTC.");
                }
            }
        }
    }
}
