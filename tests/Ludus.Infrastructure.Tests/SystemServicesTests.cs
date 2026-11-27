using System.IO.Compression;
using System.Text.Json;
using System.Text;
using Ludus.Application.Verification;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Library;
using Ludus.Infrastructure.Operations;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ludus.Infrastructure.Tests;

public sealed class SystemServicesTests
{
    [Fact]
    public async Task ManagedEmptyFoldersAreCreatedAndDeletedWithoutTouchingForeignDirectories()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        var foreign = Path.Combine(fixture.Paths.RomDirectory, "foreign");
        Directory.CreateDirectory(foreign);
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await database.QualityProfiles.SingleAsync();
        var root = await database.RootFolders.SingleAsync();
        var settings = await database.AppSettings.SingleAsync();
        settings.CreateEmptyFolders = true;
        settings.DeleteEmptyFolders = true;
        var game = new Game
        {
            Title = "Managed Example", SortTitle = "Managed Example", PlatformId = profile.PlatformId,
            RootFolderId = root.Id, QualityProfileId = profile.Id, Monitored = true
        };
        database.Games.Add(game);
        await database.SaveChangesAsync();
        var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();

        await scanner.ScanAsync(root.Id, profile.PlatformId, default);
        database.ChangeTracker.Clear();
        game = await database.Games.SingleAsync();
        Assert.NotNull(game.ManagedFolderRelativePath);
        var managed = Path.Combine(root.Path, game.ManagedFolderRelativePath!);
        Assert.True(Directory.Exists(managed));
        Assert.True(Directory.Exists(foreign));

        game.Monitored = false;
        await database.SaveChangesAsync();
        await scanner.ScanAsync(root.Id, profile.PlatformId, default);
        database.ChangeTracker.Clear();
        Assert.Null((await database.Games.SingleAsync()).ManagedFolderRelativePath);
        Assert.False(Directory.Exists(managed));
        Assert.True(Directory.Exists(foreign));
    }

    [Theory]
    [InlineData(false, VerificationStatus.VerifiedGoodDump)]
    [InlineData(true, VerificationStatus.Modified)]
    public async Task LibraryScanVerifiesDatAndRecognizesModifiedCanonicalReleases(bool modified, VerificationStatus expected)
    {
        await using var fixture = new TestDatabase(); await fixture.InitializeAsync(); Directory.CreateDirectory(fixture.Paths.RomDirectory);
        await using var scope = fixture.CreateScope(); var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var platform = await database.Platforms.SingleAsync(x => x.Slug == "nes");
        using var original = new MemoryStream("original"u8.ToArray());
        var hashes = await scope.ServiceProvider.GetRequiredService<IFileHasher>().ComputeAsync(original);
        var xml = $"<datafile><header><name>Test</name></header><game name=\"Example (USA)\"><rom name=\"Example (USA).nes\" size=\"{hashes.Size}\" crc=\"{hashes.Crc32}\" md5=\"{hashes.Md5}\" sha1=\"{hashes.Sha1}\"/></game></datafile>";
        using var dat = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        await scope.ServiceProvider.GetRequiredService<IDatImporter>().ImportAsync(platform.Id, "test.dat", dat);
        await File.WriteAllTextAsync(Path.Combine(fixture.Paths.RomDirectory, "Example (USA).nes"), modified ? "modified" : "original");
        await scope.ServiceProvider.GetRequiredService<LibraryScanner>().ScanAsync(null, platform.Id, default);
        Assert.Equal(expected, (await database.GameFiles.SingleAsync()).VerificationStatus);
    }

    [Fact]
    public async Task BackupWaitsUntilImportJournalsHaveBeenRecovered()
    {
        await using var fixture = new TestDatabase(); await fixture.InitializeAsync();
        var journals = Path.Combine(fixture.Paths.ConfigDirectory, "Imports"); Directory.CreateDirectory(journals); await File.WriteAllTextAsync(Path.Combine(journals, "1.json"), "{}");
        await using var scope = fixture.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<BackupService>().CreateAsync(false, default));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Backups.CountAsync());
    }

    [Fact]
    public async Task UploadedBackupTraversalIsRejectedWithoutChangingLiveConfiguration()
    {
        await using var fixture = new TestDatabase(); await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<BackupService>();
        var backup = await service.CreateAsync(false, default);
        byte[] original = await File.ReadAllBytesAsync(Path.Combine(fixture.Paths.ConfigDirectory, "Backups", backup.RelativePath));
        using var modified = new MemoryStream(); modified.Write(original);
        using (var archive = new ZipArchive(modified, ZipArchiveMode.Update, true))
        using (var writer = new StreamWriter(archive.CreateEntry("../escape").Open())) await writer.WriteAsync("unsafe");
        modified.Position = 0;
        var uploaded = await service.ImportAsync(modified, default);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageRestoreAsync(uploaded.Id, default));
        Assert.False(File.Exists(Path.Combine(fixture.Paths.ConfigDirectory, "restore-pending.json")));
        Assert.False(File.Exists(Path.Combine(fixture.Paths.ConfigDirectory, "restore-work", "escape")));
        Assert.True(File.Exists(fixture.Paths.DatabasePath));
    }

    [Fact]
    public async Task ScanRelocatesACompleteDiscSetAcrossRootsWithoutCreatingAnotherGame()
    {
        await using var fixture = new TestDatabase(); await fixture.InitializeAsync();
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        await File.WriteAllTextAsync(Path.Combine(fixture.Paths.RomDirectory, "Example.cue"), "FILE \"track.bin\" BINARY\n TRACK 01 MODE2/2352\n");
        await File.WriteAllTextAsync(Path.Combine(fixture.Paths.RomDirectory, "track.bin"), "game data");
        await using var scope = fixture.CreateScope(); var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var platform = await database.Platforms.SingleAsync(x => x.Slug == "ps1");
        var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
        Assert.Equal(2, (await scanner.ScanAsync(null, platform.Id, default)).Added);
        var originalId = await database.Games.Select(x => x.Id).SingleAsync();
        var target = Path.Combine(fixture.Paths.DownloadDirectory, "new-root"); Directory.CreateDirectory(target);
        var root = new RootFolder { Name = "Moved", Path = target }; database.RootFolders.Add(root); await database.SaveChangesAsync();
        File.Move(Path.Combine(fixture.Paths.RomDirectory, "Example.cue"), Path.Combine(target, "Example.cue"));
        File.Move(Path.Combine(fixture.Paths.RomDirectory, "track.bin"), Path.Combine(target, "track.bin"));
        var result = await scanner.ScanAsync(null, platform.Id, default);
        Assert.Equal(2, result.Relocated); Assert.Equal(0, result.Added); Assert.Equal(0, result.Missing);
        database.ChangeTracker.Clear();
        var game = await database.Games.SingleAsync(); Assert.Equal(originalId, game.Id); Assert.Equal(root.Id, game.RootFolderId); Assert.Equal(GameStatus.Available, game.Status);
        Assert.Equal(2, await database.GameFiles.CountAsync());
    }

    [Fact]
    public async Task UploadedBackupCanBeRestoredOnAnIndependentInstallation()
    {
        await using var original = new TestDatabase(); await original.InitializeAsync();
        byte[] bytes;
        await using (var scope = original.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await database.AppSettings.SingleAsync()).InstanceName = "Recovered instance"; await database.SaveChangesAsync();
            var backup = await scope.ServiceProvider.GetRequiredService<BackupService>().CreateAsync(false, default);
            bytes = await File.ReadAllBytesAsync(Path.Combine(original.Paths.ConfigDirectory, "Backups", backup.RelativePath));
        }
        await using var target = new TestDatabase(); await target.InitializeAsync();
        await using (var scope = target.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BackupService>();
            using var input = new MemoryStream(bytes); var imported = await service.ImportAsync(input, default);
            await service.StageRestoreAsync(imported.Id, default);
        }
        await BackupService.ApplyPendingAsync(target.Paths, default);
        await BackupService.ApplyPendingAsync(target.Paths, default);
        await using var check = target.CreateScope();
        Assert.Equal("Recovered instance", (await check.ServiceProvider.GetRequiredService<AppDbContext>().AppSettings.SingleAsync()).InstanceName);
    }

    [Fact]
    public async Task ScanFindsExistingRomsHealsMovesAndDetectsChangedOrMissingFiles()
    {
        await using var fixture = new TestDatabase(); await fixture.InitializeAsync();
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        var path = Path.Combine(fixture.Paths.RomDirectory, "Example (USA).nes"); await File.WriteAllTextAsync(path, "original");
        await using var scope = fixture.CreateScope(); var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var platform = await database.Platforms.SingleAsync(x => x.Slug == "nes");
        var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
        Assert.Equal(1, (await scanner.ScanAsync(null, platform.Id, default)).Added);
        var original = await database.GameFiles.AsNoTracking().SingleAsync();
        var moved = Path.Combine(fixture.Paths.RomDirectory, "Renamed (USA).nes"); File.Move(path, moved);
        Assert.Equal(1, (await scanner.ScanAsync(null, platform.Id, default)).Relocated);
        database.ChangeTracker.Clear();
        var healed = await database.GameFiles.AsNoTracking().SingleAsync(); Assert.Equal(original.Id, healed.Id); Assert.Equal("Renamed (USA).nes", healed.RelativePath);
        Assert.Equal(1, await database.Games.CountAsync());
        await File.WriteAllTextAsync(moved, "changed"); await scanner.ScanAsync(null, platform.Id, default);
        database.ChangeTracker.Clear(); Assert.Equal(VerificationStatus.Modified, (await database.GameFiles.SingleAsync()).VerificationStatus);
        File.Delete(moved); Assert.Equal(1, (await scanner.ScanAsync(null, platform.Id, default)).Missing);
        database.ChangeTracker.Clear(); Assert.True((await database.GameFiles.SingleAsync()).Missing); Assert.Equal(GameStatus.Missing, (await database.Games.SingleAsync()).Status);
    }

    [Fact]
    public async Task BackupIsConsistentAndRestoreRecoversConfigurationAndEncryptedCredentials()
    {
        await using var fixture = new TestDatabase(); await fixture.InitializeAsync();
        long backupId; string archivePath; Guid session;
        await using (var scope = fixture.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await database.AppSettings.SingleAsync(); settings.InstanceName = "Before";
            database.DownloadClients.Add(new DownloadClient { Name = "Client", BaseUrl = "http://client", Password = fixture.Secrets.Protect("restorable-password") });
            await database.SaveChangesAsync();
            session = (await database.SecuritySettings.SingleAsync()).SessionVersion;
            var service = scope.ServiceProvider.GetRequiredService<BackupService>();
            var backup = await service.CreateAsync(false, default); backupId = backup.Id;
            archivePath = Path.Combine(fixture.Paths.ConfigDirectory, "Backups", backup.RelativePath);
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                Assert.Contains(zip.Entries, x => x.FullName == "ludus.db"); Assert.Contains(zip.Entries, x => x.FullName.StartsWith("keys/"));
                Assert.DoesNotContain(zip.Entries, x => x.FullName.Contains("-wal"));
            }
            settings.InstanceName = "After"; await database.SaveChangesAsync();
            await service.StageRestoreAsync(backupId, default);
        }
        var journals = Path.Combine(fixture.Paths.ConfigDirectory, "Imports"); Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(Path.Combine(journals, "999.json"), "{}");
        await BackupService.ApplyPendingAsync(fixture.Paths, default);
        await using (var scope = fixture.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("Before", (await database.AppSettings.SingleAsync()).InstanceName);
            Assert.NotEqual(session, (await database.SecuritySettings.SingleAsync()).SessionVersion);
            Assert.Equal("restorable-password", fixture.Secrets.Unprotect((await database.DownloadClients.SingleAsync()).Password!));
            Assert.False(File.Exists(Path.Combine(fixture.Paths.ConfigDirectory, "restore-pending.json")));
        }
        Assert.True(File.Exists(archivePath));
        Assert.False(Directory.Exists(journals));
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Paths.ConfigDirectory, "restore-previous"), "999.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task BackupRetentionKeepsNewestScheduledArchiveAndTamperingBlocksRestore()
    {
        await using var fixture = new TestDatabase(); await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope(); var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await database.AppSettings.SingleAsync()).BackupRetentionCount = 1; await database.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<BackupService>();
        var first = await service.CreateAsync(true, default); var second = await service.CreateAsync(true, default);
        Assert.False(File.Exists(Path.Combine(fixture.Paths.ConfigDirectory, "Backups", first.RelativePath)));
        Assert.Equal(1, await database.Backups.CountAsync());
        await File.AppendAllTextAsync(Path.Combine(fixture.Paths.ConfigDirectory, "Backups", second.RelativePath), "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageRestoreAsync(second.Id, default));
        Assert.False(File.Exists(Path.Combine(fixture.Paths.ConfigDirectory, "restore-pending.json")));
    }

    [Fact]
    public async Task DiagnosticsExportContainsOnlyRedactedLogsAndSafeSystemData()
    {
        await using var fixture = new TestDatabase(); await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope(); var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.DownloadClients.Add(new DownloadClient { Name = "Client", BaseUrl = "http://client", Password = fixture.Secrets.Protect("diagnostics-password") });
        await database.SaveChangesAsync();
        var apiKey = fixture.Secrets.Unprotect((await database.SecuritySettings.SingleAsync()).ApiKey);
        await File.WriteAllTextAsync(Path.Combine(fixture.Paths.LogsDirectory, "ludus.txt"), $"password=diagnostics-password api_key={apiKey} https://webhook.test/secret-token\n");
        var bytes = await scope.ServiceProvider.GetRequiredService<DiagnosticsService>().ExportAsync(default);
        using var stream = new MemoryStream(bytes); using var zip = new ZipArchive(stream);
        Assert.Equal(3, zip.Entries.Count);
        Assert.DoesNotContain(zip.Entries, x => x.FullName.Contains("keys") || x.FullName.EndsWith(".db"));
        using var reader = new StreamReader(zip.GetEntry("logs/ludus.txt")!.Open()); var text = await reader.ReadToEndAsync();
        Assert.DoesNotContain("diagnostics-password", text); Assert.DoesNotContain(apiKey, text); Assert.DoesNotContain("secret-token", text);
        Assert.Contains("REDACTED", text);
    }

    [Fact]
    public async Task HealthChecksPersistAndClearResolvedWarnings()
    {
        await using var fixture = new TestDatabase(); await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope(); var health = scope.ServiceProvider.GetRequiredService<HealthService>();
        var first = await health.CheckAsync(default); Assert.Contains(first, x => x.Code == "RootNotWritable"); Assert.Contains(first, x => x.Code == "NoRssIndexer");
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        var second = await health.CheckAsync(default); Assert.DoesNotContain(second, x => x.Code == "RootNotWritable");
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(second.Count, await database.HealthIssues.CountAsync());
    }
}
