using System.Text.Json;
using Ludus.Domain.Activity;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Ludus.Infrastructure.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task MigrationCreatesCompleteSchemaInWalMode()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(2, (await database.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
        Assert.False(database.Database.HasPendingModelChanges());
        Assert.Equal(22, await database.Platforms.CountAsync());
        Assert.Equal(6, await database.CustomFormats.CountAsync());
        Assert.Equal(6, await database.ScheduledTasks.CountAsync());
        Assert.Equal(2, await database.MetadataProviders.CountAsync());
        Assert.Equal(fixture.Paths.RomDirectory, (await database.RootFolders.SingleAsync()).Path);
        Assert.False(Directory.Exists(fixture.Paths.RomDirectory));
        Assert.False(Directory.Exists(fixture.Paths.DownloadDirectory));

        await database.Database.OpenConnectionAsync();
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", await command.ExecuteScalarAsync());
        command.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        command.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task RestartPreservesApiKeyAndUserConfiguration()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        string apiKey;
        await using (var scope = fixture.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await database.AppSettings.SingleAsync();
            settings.Language = "en";
            var profile = await database.QualityProfiles.SingleAsync();
            profile.UpgradeUntilScore = 250;
            apiKey = fixture.Secrets.Unprotect((await database.SecuritySettings.SingleAsync()).ApiKey);
            Assert.Equal(64, apiKey.Length);
            await database.SaveChangesAsync();
        }

        await fixture.InitializeAsync();
        await using var secondScope = fixture.CreateScope();
        var reopened = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("en", (await reopened.AppSettings.SingleAsync()).Language);
        Assert.Equal(250, (await reopened.QualityProfiles.SingleAsync()).UpgradeUntilScore);
        Assert.Equal(apiKey, fixture.Secrets.Unprotect((await reopened.SecuritySettings.SingleAsync()).ApiKey));
        Assert.Equal(22, await reopened.Platforms.CountAsync());
    }

    [Fact]
    public async Task StoredCredentialsAreEncryptedAndNotSerialized()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const string password = "secret-only-for-persistence-test";
        database.DownloadClients.Add(new DownloadClient
        {
            Name = "Test client", BaseUrl = "http://127.0.0.1:9091", Kind = DownloadClientKind.Transmission,
            Password = fixture.Secrets.Protect(password)
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var stored = (await database.DownloadClients.SingleAsync()).Password!;
        Assert.Equal(password, fixture.Secrets.Unprotect(stored));
        Assert.DoesNotContain(password, JsonSerializer.Serialize(stored));
        Assert.DoesNotContain(stored.Ciphertext, JsonSerializer.Serialize(stored));
        Assert.Equal("[REDACTED]", stored.ToString());

        await database.Database.OpenConnectionAsync();
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT Password FROM DownloadClients;";
        var persisted = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.NotEqual(password, persisted);
        Assert.Equal(stored.Ciphertext, persisted);
        Assert.NotEmpty(Directory.GetFiles(fixture.Paths.KeysDirectory, "*.xml"));
    }

    [Fact]
    public async Task PersistedKeyRingDecryptsSecretsFromAnEarlierProcess()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        var secret = fixture.Secrets.Protect("cross-process-credential");
        await using var independentServices = new ServiceCollection().AddLogging().AddInfrastructure(fixture.Paths).BuildServiceProvider();
        var protector = independentServices.GetRequiredService<Ludus.Application.Security.ISecretProtector>();
        Assert.Equal("cross-process-credential", protector.Unprotect(secret));
    }

    [Fact]
    public async Task OptimisticConcurrencyRejectsLostUpdates()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var firstScope = fixture.CreateScope();
        await using var secondScope = fixture.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstSettings = await first.AppSettings.SingleAsync();
        var secondSettings = await second.AppSettings.SingleAsync();
        firstSettings.InstanceName = "First change";
        await first.SaveChangesAsync();
        secondSettings.InstanceName = "Lost update";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task RootFolderAndProfileCannotBeDeletedWhileReferenced()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = await AddGameAsync(database);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            database.RootFolders.Where(x => x.Id == game.RootFolderId).ExecuteDeleteAsync());
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            database.QualityProfiles.Where(x => x.Id == game.QualityProfileId).ExecuteDeleteAsync());
    }

    [Theory]
    [InlineData("../escape.nes")]
    [InlineData("/absolute.nes")]
    [InlineData("C:/absolute.nes")]
    [InlineData("game/../../escape.nes")]
    [InlineData("game\\escape.nes")]
    public async Task FilePathsMustStayRelativeToRootFolder(string path)
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = await AddGameAsync(database);
        database.GameFiles.Add(new GameFile
        {
            GameId = game.Id, RelativePath = path, OriginalFileName = "game.nes", ReleaseName = "Example (Europe)",
            FileGroupId = Guid.NewGuid(), ImportedAtUtc = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task DownloadCategoryCannotEscapeLudus()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.DownloadClients.Add(new DownloadClient { Name = "Wrong category", BaseUrl = "http://localhost:8081", Category = "radarr" });
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task InvalidCutoffAndCredentialFreeFormsLoginAreRejected()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await database.QualityProfiles.SingleAsync();
        profile.MinimumScore = profile.UpgradeUntilScore + 1;
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        (await database.SecuritySettings.SingleAsync()).FormsAuthenticationEnabled = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task CooldownRequiresExpiryAndPermanentBlockForbidsExpiry()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = await AddGameAsync(database);
        var block = new ReleaseBlock
        {
            GameId = game.Id, ReleaseFingerprint = "example-release", ReleaseTitle = "Example", ReasonCode = "network",
            Kind = BlockKind.Cooldown
        };
        database.ReleaseBlocks.Add(block);
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        block.ExpiresAtUtc = DateTime.UtcNow.AddDays(7);
        await database.SaveChangesAsync();
        block.Kind = BlockKind.Permanent;
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        block.ExpiresAtUtc = null;
        await database.SaveChangesAsync();
    }

    [Fact]
    public async Task JsonCollectionsTrackInPlaceChangesAndDatesRoundTripAsUtc()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var platform = await database.Platforms.SingleAsync(x => x.Slug == "nes");
        platform.AllowedExtensions.Add(".fds");
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        platform = await database.Platforms.SingleAsync(x => x.Slug == "nes");
        Assert.Contains(".fds", platform.AllowedExtensions);
        Assert.Equal(DateTimeKind.Utc, platform.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, platform.UpdatedAtUtc.Kind);
    }

    [Fact]
    public async Task MigrationCanBeRolledBackAndReapplied()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using (var scope = fixture.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.GetService<IMigrator>().MigrateAsync("0");
            Assert.Empty(await database.Database.GetAppliedMigrationsAsync());
        }

        await fixture.InitializeAsync();
        await using var newScope = fixture.CreateScope();
        Assert.Equal(22, await newScope.ServiceProvider.GetRequiredService<AppDbContext>().Platforms.CountAsync());
    }

    private static async Task<Game> AddGameAsync(AppDbContext database)
    {
        var game = new Game
        {
            Title = "Example", SortTitle = "Example",
            PlatformId = (await database.Platforms.FirstAsync()).Id,
            RootFolderId = (await database.RootFolders.SingleAsync()).Id,
            QualityProfileId = (await database.QualityProfiles.SingleAsync()).Id
        };
        database.Games.Add(game);
        await database.SaveChangesAsync();
        return game;
    }
}
