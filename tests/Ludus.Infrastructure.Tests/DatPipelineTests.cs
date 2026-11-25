using System.Text;
using Ludus.Application.Verification;
using Ludus.Domain.Common;
using Ludus.Infrastructure.Persistence;
using Ludus.Infrastructure.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ludus.Infrastructure.Tests;

public sealed class DatPipelineTests
{
    private const string Crc = "cbf43926";
    private const string Md5 = "25f9e794323b453885f5181f1b624d0b";
    private const string Sha1 = "f7c3bc1d808e04732adf679965ccc34ca7ae3441";

    [Theory]
    [InlineData("123456789", Crc, Md5, Sha1)]
    [InlineData("", "00000000", "d41d8cd98f00b204e9800998ecf8427e", "da39a3ee5e6b4b0d3255bfef95601890afd80709")]
    public async Task HashesMatchIndependentKnownVectors(string input, string crc, string md5, string sha1)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(input));
        var hashes = await new FileHasher().ComputeAsync(stream);
        Assert.Equal(new FileHashes(input.Length, crc, md5, sha1), hashes);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ImportsDatWithExternalDoctypeWithoutFetchingItAndRecognizesRenamedRom()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var platform = await database.Platforms.FirstAsync();
        var importer = scope.ServiceProvider.GetRequiredService<IDatImporter>();
        var xml = "<!DOCTYPE datafile SYSTEM 'https://invalid.example/unreachable.dtd'>" + Dat();
        using var content = Content(xml);
        var result = await importer.ImportAsync(platform.Id, "../../No-Intro.dat", content);
        Assert.Equal(1, result.Games);
        Assert.Equal(1, result.Roms);
        Assert.Equal("No-Intro.dat", (await database.DatFiles.SingleAsync()).FileName);
        var verifier = scope.ServiceProvider.GetRequiredService<IDatVerifier>();
        var match = await verifier.VerifyAsync(platform.Id, new FileHashes(9, Crc.ToUpperInvariant(), Md5, Sha1));
        Assert.Equal(VerificationStatus.VerifiedGoodDump, match.Status);
        Assert.Equal("Example (Europe)", Assert.Single(match.Matches).CanonicalName);
        using var duplicate = Content(xml);
        Assert.True((await importer.ImportAsync(platform.Id, "another-name.dat", duplicate)).AlreadyImported);
        Assert.Single(await database.DatFiles.ToArrayAsync());
    }

    [Theory]
    [InlineData("baddump", VerificationStatus.Modified, "KnownBadDump")]
    [InlineData("nodump", VerificationStatus.Unverified, "InsufficientTrustedChecksums")]
    [InlineData("good", VerificationStatus.VerifiedGoodDump, "VerifiedGoodDump")]
    public async Task DumpStatusIsNotIgnored(string status, VerificationStatus expected, string reason)
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var platform = await database.Platforms.FirstAsync();
        using var content = Content(Dat(status));
        await scope.ServiceProvider.GetRequiredService<IDatImporter>().ImportAsync(platform.Id, "test.dat", content);
        var result = await scope.ServiceProvider.GetRequiredService<IDatVerifier>().VerifyAsync(platform.Id, new FileHashes(9, Crc, Md5, Sha1));
        Assert.Equal(expected, result.Status);
        Assert.Equal(reason, result.ReasonCode);
    }

    [Fact]
    public async Task CrcOnlyCannotVerifyAndConflictingStrongHashesReject()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var platform = await database.Platforms.FirstAsync();
        using var crcOnly = Content(Dat().Replace($"md5=\"{Md5}\" sha1=\"{Sha1}\"", ""));
        await scope.ServiceProvider.GetRequiredService<IDatImporter>().ImportAsync(platform.Id, "crc.dat", crcOnly);
        var verifier = scope.ServiceProvider.GetRequiredService<IDatVerifier>();
        Assert.Equal(VerificationStatus.Unverified, (await verifier.VerifyAsync(platform.Id, new FileHashes(9, Crc, Md5, Sha1))).Status);
        await database.DatFiles.ExecuteDeleteAsync();
        using var full = Content(Dat());
        await scope.ServiceProvider.GetRequiredService<IDatImporter>().ImportAsync(platform.Id, "full.dat", full);
        Assert.Equal(VerificationStatus.Modified,
            (await verifier.VerifyAsync(platform.Id, new FileHashes(9, Crc, new string('0', 32), Sha1))).Status);
        Assert.Equal(VerificationStatus.Modified,
            (await verifier.VerifyAsync(platform.Id, new FileHashes(10, Crc, Md5, Sha1))).Status);
    }

    [Fact]
    public async Task DisabledDatsAndOtherPlatformsNeverMatch()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var platforms = await database.Platforms.Take(2).ToArrayAsync();
        using var content = Content(Dat());
        await scope.ServiceProvider.GetRequiredService<IDatImporter>().ImportAsync(platforms[0].Id, "test.dat", content);
        var verifier = scope.ServiceProvider.GetRequiredService<IDatVerifier>();
        Assert.Equal(VerificationStatus.Unverified, (await verifier.VerifyAsync(platforms[1].Id, new FileHashes(9, Crc, Md5, Sha1))).Status);
        await database.DatFiles.ExecuteUpdateAsync(update => update.SetProperty(x => x.Enabled, false));
        Assert.Equal(VerificationStatus.Unverified, (await verifier.VerifyAsync(platforms[0].Id, new FileHashes(9, Crc, Md5, Sha1))).Status);
    }

    [Fact]
    public async Task MalformedLateInputRollsBackAlreadyFlushedBatches()
    {
        await using var fixture = new TestDatabase();
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var platform = await database.Platforms.FirstAsync();
        var games = string.Concat(Enumerable.Range(0, 110).Select(i => $"<game name='Game {i}'><rom name='a' size='9' crc='{Crc}'/></game>"));
        using var content = Content($"<datafile>{games}<game name='Broken'><rom name='bad' crc='xyz'/></game></datafile>");
        await Assert.ThrowsAsync<FormatException>(() => scope.ServiceProvider.GetRequiredService<IDatImporter>().ImportAsync(platform.Id, "bad.dat", content));
        Assert.Empty(await database.DatFiles.ToArrayAsync());
        Assert.Empty(await database.DatGames.ToArrayAsync());
        Assert.Empty(await database.DatRoms.ToArrayAsync());
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Paths.ConfigDirectory, "Temporary")));
    }

    [Theory]
    [InlineData("<!DOCTYPE datafile [<!ENTITY x SYSTEM 'file:///never-read'>]><datafile><game name='&x;'><rom name='a' crc='00000000'/></game></datafile>")]
    [InlineData("<datafile><game name='Example'><disk name='x' sha1='0000000000000000000000000000000000000000'/></game></datafile>")]
    [InlineData("<datafile><game name='Example'><rom name='x' crc='no-hash'/></game></datafile>")]
    public async Task RejectsEntitiesDiskHashesAndInvalidChecksums(string xml)
    {
        using var content = Content(xml);
        var reader = new LogiqxDatReader();
        var exception = await Record.ExceptionAsync(async () => { await foreach (var _ in reader.ReadAsync(content)) { } });
        Assert.True(exception is FormatException or System.Xml.XmlException);
    }

    [Fact]
    public async Task SupportsMachineAliasAndNoDumpWithoutHashes()
    {
        using var content = Content("<datafile><machine name='Arcade'><rom name='missing.bin' status='nodump'/></machine></datafile>");
        var records = new List<DatRecord>();
        await foreach (var record in new LogiqxDatReader().ReadAsync(content)) records.Add(record);
        Assert.Equal("nodump", Assert.Single(Assert.IsType<DatGameRecord>(Assert.Single(records)).Game.Roms).Status);
    }

    private static MemoryStream Content(string value) => new(Encoding.UTF8.GetBytes(value));
    private static string Dat(string status = "good") => $"""
        <datafile><header><name>No-Intro Test</name><version>1</version></header>
        <game name="Example (Europe)"><description>Example</description>
        <rom name="example.nes" size="9" crc="{Crc}" md5="{Md5}" sha1="{Sha1}" status="{status}"/>
        </game></datafile>
        """;
}
