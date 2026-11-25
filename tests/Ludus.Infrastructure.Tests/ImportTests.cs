using System.IO.Compression;
using Ludus.Domain.Activity;
using Ludus.Domain.Common;
using Ludus.Domain.Configuration;
using Ludus.Domain.Integrations;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Verification;
using Ludus.Infrastructure.Persistence;
using Ludus.Application.Verification;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ludus.Infrastructure.Tests;

public sealed class ImportTests
{
    [Fact]
    public async Task ImportIsIdempotentRetainsSeedSourceAndRecyclesOnUpgrade()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        Directory.CreateDirectory(fixture.Paths.DownloadDirectory);
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var download = await SeedAsync(database, "nes");
        var first = Path.Combine(fixture.Paths.DownloadDirectory, "Example (USA).nes");
        await File.WriteAllTextAsync(first, "first rom");
        var importer = scope.ServiceProvider.GetRequiredService<ImportService>();
        await importer.ImportAsync(download.Id, first, default);
        await importer.ImportAsync(download.Id, first, default);
        Assert.True(File.Exists(first));
        database.ChangeTracker.Clear();
        var original = await database.GameFiles.SingleAsync();
        Assert.Equal(80, original.Score);
        Assert.Equal("first rom", await File.ReadAllTextAsync(Path.Combine(fixture.Paths.RomDirectory, original.RelativePath)));
        var upgrade = new TrackedDownload
        {
            GameId = download.GameId, DownloadClientId = download.DownloadClientId, DownloadId = "upgrade", ReleaseGuid = "upgrade", ReleaseFingerprint = "upgrade",
            ReleaseTitle = "Example (Europe).nes", Score = 100, Status = DownloadStatus.Completed
        };
        database.Downloads.Add(upgrade);
        await database.SaveChangesAsync();
        var second = Path.Combine(fixture.Paths.DownloadDirectory, "Example (Europe).nes");
        await File.WriteAllTextAsync(second, "better rom");
        await importer.ImportAsync(upgrade.Id, second, default);
        database.ChangeTracker.Clear();
        Assert.Equal(100, (await database.GameFiles.SingleAsync()).Score);
        Assert.Equal("first rom", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(fixture.Paths.RecyclingDirectory, "*", SearchOption.AllDirectories))));
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(2, await database.History.CountAsync());
        Assert.Equal(HistoryEvent.Upgraded, (await database.History.OrderBy(x => x.Id).LastAsync()).Event);
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Paths.ConfigDirectory, "Imports")));
    }

    [Fact]
    public async Task CueSetsAreCompleteAndMultiDiscPlaylistReferencesExistingDescriptors()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        var source = Path.Combine(fixture.Paths.DownloadDirectory, "job");
        Directory.CreateDirectory(source);
        for (var disc = 1; disc <= 2; disc++)
        {
            await File.WriteAllTextAsync(Path.Combine(source, $"Example (Disc {disc}).cue"), $"FILE \"track{disc}.bin\" BINARY\n TRACK 01 MODE2/2352\n");
            await File.WriteAllTextAsync(Path.Combine(source, $"track{disc}.bin"), $"disc {disc}");
        }
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var download = await SeedAsync(database, "ps1");
        await scope.ServiceProvider.GetRequiredService<ImportService>().ImportAsync(download.Id, source, default);
        database.ChangeTracker.Clear();
        var files = await database.GameFiles.ToArrayAsync();
        Assert.Equal(5, files.Length);
        Assert.Equal(2, files.Count(x => x.Role == FileRole.Track));
        var playlist = Path.Combine(fixture.Paths.RomDirectory, Assert.Single(files, x => x.Role == FileRole.Playlist).RelativePath);
        foreach (var line in await File.ReadAllLinesAsync(playlist)) Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(playlist)!, line)));
    }

    [Fact]
    public async Task MissingCueTrackFailsBeforePublishingFiles()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        Directory.CreateDirectory(fixture.Paths.DownloadDirectory);
        var source = Path.Combine(fixture.Paths.DownloadDirectory, "Example.cue");
        await File.WriteAllTextAsync(source, "FILE \"missing.bin\" BINARY\n");
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var download = await SeedAsync(database, "ps1");
        var exception = await Assert.ThrowsAsync<ImportException>(() => scope.ServiceProvider.GetRequiredService<ImportService>().ImportAsync(download.Id, source, default));
        Assert.True(exception.Permanent);
        Assert.Equal("MissingDiscTrack", exception.Code);
        Assert.Empty(await database.GameFiles.ToArrayAsync());
    }

    [Fact]
    public async Task ArchiveTraversalAndExpansionLimitsAreRejected()
    {
        await using var fixture = new TestDatabase();
        var archivePath = Path.Combine(fixture.Paths.ConfigDirectory, "bad.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("../escape.nes").Open())) await writer.WriteAsync("bad");
        var destination = Path.Combine(fixture.Paths.ConfigDirectory, "extract");
        var settings = new AppSettings { RecyclingBinPath = fixture.Paths.RecyclingDirectory, MaximumExtractedBytes = 4 };
        Assert.Equal("UnsafePath", (await Assert.ThrowsAsync<ImportException>(() => new ArchiveExtractor(new FileHasher()).ExtractAsync(archivePath, destination, settings, default))).Code);
        File.Delete(archivePath);
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("large.nes").Open())) await writer.WriteAsync(new string('a', 100));
        Assert.Equal("ArchiveSizeLimit", (await Assert.ThrowsAsync<ImportException>(() => new ArchiveExtractor(new FileHasher()).ExtractAsync(archivePath, destination, settings, default))).Code);
        Assert.False(File.Exists(Path.Combine(fixture.Paths.ConfigDirectory, "escape.nes")));
    }

    [Fact]
    public async Task ZipExtractionAndSwitchDlcStayOnTheExistingGame()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        Directory.CreateDirectory(fixture.Paths.DownloadDirectory);
        var archivePath = Path.Combine(fixture.Paths.DownloadDirectory, "dlc.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("Example [01007EF00011F001].nsp").Open())) await writer.WriteAsync("dlc data");
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var download = await SeedAsync(database, "switch");
        download.Game.Platform.ExtractArchives = true;
        download.Game.BaseTitleId = "01007EF00011E000";
        await database.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<ImportService>().ImportAsync(download.Id, archivePath, default);
        database.ChangeTracker.Clear();
        Assert.Equal(1, await database.Games.CountAsync());
        var file = await database.GameFiles.SingleAsync();
        Assert.Equal(ContentKind.Dlc, file.ContentKind);
        Assert.Contains("DLC/01007EF00011F001", file.RelativePath);
        Assert.Equal(download.GameId, file.GameId);
        Assert.Equal(GameStatus.Missing, (await database.Games.SingleAsync()).Status);
    }

    [Fact]
    public async Task RemoteMappingsRequireBoundaryAndUseLongestHostMatch()
    {
        await using var fixture = new TestDatabase();
        var resolver = new RemotePathResolver(fixture.Paths);
        RemotePathMapping[] mappings =
        [
            new() { Host = "CLIENT", RemotePath = "/remote", LocalPath = fixture.Paths.DownloadDirectory },
            new() { Host = "client", RemotePath = "/remote/special", LocalPath = Path.Combine(fixture.Paths.DownloadDirectory, "mapped") }
        ];
        Assert.Equal(Path.Combine(fixture.Paths.DownloadDirectory, "mapped", "a.nes"), resolver.Resolve("client", "/remote/special/a.nes", mappings).Path);
        Assert.Throws<ImportException>(() => resolver.Resolve("client", "/remote-other/a.nes", mappings));
        Assert.Throws<ImportException>(() => resolver.Resolve("other", "/remote/a.nes", mappings));
        Assert.Throws<ImportException>(() => resolver.Resolve("client", "/remote/../secret", mappings));
    }

    [Fact]
    public async Task RealSevenZipFixtureExtractsWithManagedDecoder()
    {
        await using var fixture = new TestDatabase();
        var destination = Path.Combine(fixture.Paths.ConfigDirectory, "sevenzip");
        await new ArchiveExtractor(new FileHasher()).ExtractAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EmptyStream.7z"), destination,
            new AppSettings { RecyclingBinPath = fixture.Paths.RecyclingDirectory }, default);
        Assert.NotEmpty(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RecoveryDoesNotDeletePublishedReplacementWithIdenticalHash()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        Directory.CreateDirectory(fixture.Paths.DownloadDirectory);
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var download = await SeedAsync(database, "nes");
        var source = Path.Combine(fixture.Paths.DownloadDirectory, "Example (USA).nes");
        await File.WriteAllTextAsync(source, "unchanged bytes");
        var importer = scope.ServiceProvider.GetRequiredService<ImportService>();
        await importer.ImportAsync(download.Id, source, default);
        database.ChangeTracker.Clear();
        var old = await database.GameFiles.SingleAsync();
        var upgrade = new TrackedDownload { GameId = download.GameId, DownloadClientId = download.DownloadClientId, DownloadId = "recovery",
            ReleaseGuid = "recovery", ReleaseFingerprint = "recovery", ReleaseTitle = "Example (Europe)", Status = DownloadStatus.Importing };
        database.Downloads.Add(upgrade);
        await database.SaveChangesAsync();
        var stage = Path.Combine(fixture.Paths.RomDirectory, ".ludus-staging", "recovery");
        Directory.CreateDirectory(stage);
        var recycledRelative = $"{upgrade.Id}/old.nes";
        var recycled = SafePaths.Within(fixture.Paths.RecyclingDirectory, recycledRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(recycled)!);
        File.Copy(source, recycled);
        var replacement = new GameFile { GameId = download.GameId, RelativePath = old.RelativePath, OriginalFileName = old.OriginalFileName,
            ReleaseName = "Example (Europe)", FileGroupId = Guid.NewGuid(), Size = old.Size, Sha1 = old.Sha1, Md5 = old.Md5, Crc32 = old.Crc32, Score = 100,
            ImportedAtUtc = DateTime.UtcNow };
        var journal = new ImportJournal(upgrade.Id, download.GameId, fixture.Paths.RomDirectory, stage, fixture.Paths.RecyclingDirectory,
            [new ImportFile(replacement, "files/already-published")], [new RecycledFile(old.Id, old.RelativePath, recycledRelative, old.Sha1!)], true);
        await AtomicFiles.WriteJsonAsync(Path.Combine(fixture.Paths.ConfigDirectory, "Imports", $"{upgrade.Id}.json"), journal, default);
        await importer.RecoverPendingAsync(default);
        database.ChangeTracker.Clear();
        Assert.Equal(100, (await database.GameFiles.SingleAsync()).Score);
        Assert.Equal("unchanged bytes", await File.ReadAllTextAsync(Path.Combine(fixture.Paths.RomDirectory, old.RelativePath)));
        Assert.Equal(DownloadStatus.Imported, (await database.Downloads.SingleAsync(x => x.Id == upgrade.Id)).Status);
    }

    [Fact]
    public async Task HardlinksAndCopyFallbackHaveDifferentFileIdentity()
    {
        await using var fixture = new TestDatabase();
        var source = Path.Combine(fixture.Paths.ConfigDirectory, "source");
        var link = Path.Combine(fixture.Paths.ConfigDirectory, "link");
        var copy = Path.Combine(fixture.Paths.ConfigDirectory, "copy");
        await File.WriteAllTextAsync(source, "first");
        var operations = new AtomicFiles();
        Assert.True(await operations.StageAsync(source, link, true, default));
        Assert.False(await operations.StageAsync(source, copy, false, default));
        await File.WriteAllTextAsync(link, "second");
        Assert.Equal("second", await File.ReadAllTextAsync(source));
        Assert.Equal("first", await File.ReadAllTextAsync(copy));
    }

    [Theory]
    [InlineData("ps3", "PS3_GAME/PARAM.SFO", "PS3_GAME/USRDIR/EBOOT.BIN")]
    [InlineData("wii-u", "code/main.rpx", "content/assets/data.bin")]
    public async Task FolderGamesPreserveHierarchyAndUseReleaseQuality(string platform, string first, string second)
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        var source = Path.Combine(fixture.Paths.DownloadDirectory, "folder-game");
        foreach (var relative in new[] { first, second })
        {
            var path = Path.Combine(source, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, relative);
        }
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var download = await SeedAsync(database, platform);
        await scope.ServiceProvider.GetRequiredService<ImportService>().ImportAsync(download.Id, source, default);
        database.ChangeTracker.Clear();
        var imported = await database.GameFiles.ToArrayAsync();
        Assert.Equal(2, imported.Length);
        Assert.Single(imported, x => x.Role == FileRole.Rom);
        Assert.Equal(80, imported.Single(x => x.Role == FileRole.Rom).Score);
        Assert.Single(imported.Select(x => x.FileGroupId).Distinct());
        foreach (var relative in new[] { first, second })
        {
            var file = Assert.Single(imported, x => x.RelativePath.EndsWith(relative, StringComparison.Ordinal));
            Assert.Equal(relative, await File.ReadAllTextAsync(Path.Combine(fixture.Paths.RomDirectory, file.RelativePath)));
        }
    }

    [Fact]
    public async Task GdiImportsEveryReferencedTrackIncludingRawAudio()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        Directory.CreateDirectory(fixture.Paths.RomDirectory);
        var source = Path.Combine(fixture.Paths.DownloadDirectory, "gdi-game");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "Example.gdi"), "2\n1 0 4 2352 track01.bin 0\n2 45000 0 2352 \"audio track.raw\" 0\n");
        using (var archive = ZipFile.Open(Path.Combine(source, "tracks.zip"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("track01.bin").Open())) await writer.WriteAsync("game bytes");
        await File.WriteAllTextAsync(Path.Combine(source, "audio track.raw"), "audio bytes");
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var download = await SeedAsync(database, "dreamcast");
        download.Game.Platform.ExtractArchives = true;
        await database.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<ImportService>().ImportAsync(download.Id, source, default);
        database.ChangeTracker.Clear();
        var files = await database.GameFiles.ToArrayAsync();
        Assert.Equal(3, files.Length);
        Assert.Equal(2, files.Count(x => x.Role == FileRole.Track));
        Assert.Single(files.Select(x => x.FileGroupId).Distinct());
        var descriptor = Path.Combine(fixture.Paths.RomDirectory, files.Single(x => x.Role == FileRole.Descriptor).RelativePath);
        Assert.Equal(2, (await new DescriptorFiles().ReferencesAsync(descriptor, fixture.Paths.RomDirectory, default)).Count);
    }

    [Fact]
    public async Task CorruptedStoredZipIsRejectedByChecksum()
    {
        await using var fixture = new TestDatabase();
        var path = Path.Combine(fixture.Paths.ConfigDirectory, "corrupt.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var output = archive.CreateEntry("test.nes", CompressionLevel.NoCompression).Open())
            output.Write("unique-payload-for-crc"u8);
        var bytes = await File.ReadAllBytesAsync(path);
        var position = bytes.AsSpan().IndexOf("unique-payload-for-crc"u8);
        Assert.True(position >= 0);
        bytes[position] ^= 0x01;
        await File.WriteAllBytesAsync(path, bytes);
        var error = await Assert.ThrowsAsync<ImportException>(() => new ArchiveExtractor(new FileHasher()).ExtractAsync(path,
            Path.Combine(fixture.Paths.ConfigDirectory, "corrupt-extracted"), new AppSettings { RecyclingBinPath = fixture.Paths.RecyclingDirectory }, default));
        Assert.True(error.Permanent);
        Assert.Equal("ArchiveChecksumMismatch", error.Code);
    }

    internal static async Task<TrackedDownload> SeedAsync(AppDbContext database, string platformSlug)
    {
        var game = new Game { Title = "Example", SortTitle = "Example", ReleaseYear = 1995, Platform = await database.Platforms.SingleAsync(x => x.Slug == platformSlug),
            RootFolder = await database.RootFolders.SingleAsync(), QualityProfile = await database.QualityProfiles.SingleAsync() };
        var client = new DownloadClient { Name = "Test", BaseUrl = "http://client:8080", Kind = DownloadClientKind.QBittorrent };
        var download = new TrackedDownload
        {
            Game = game, DownloadClient = client, DownloadId = "first", ReleaseGuid = "first", ReleaseFingerprint = "first",
            ReleaseTitle = "Example (USA)", Score = 80, Status = DownloadStatus.Completed
        };
        database.Downloads.Add(download);
        await database.SaveChangesAsync();
        return download;
    }
}
