using System.Security.Cryptography;
using Ludus.Application.Verification;
using Ludus.Domain.Verification;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ludus.Infrastructure.Verification;

public sealed class DatImporter(IDbContextFactory<AppDbContext> databaseFactory, LogiqxDatReader reader,
    LudusPaths paths, ILogger<DatImporter> logger) : IDatImporter
{
    public async Task<DatImportResult> ImportAsync(long platformId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var safeName = fileName.Replace('\\', '/').Split('/')[^1];
        if (safeName.Length is 0 or > 255) throw new ArgumentException("Invalid DAT filename.", nameof(fileName));
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        if (!await database.Platforms.AnyAsync(x => x.Id == platformId, cancellationToken))
            throw new ArgumentException("Platform does not exist.", nameof(platformId));

        var temporaryDirectory = Path.Combine(paths.ConfigDirectory, "Temporary");
        Directory.CreateDirectory(temporaryDirectory);
        await using var spool = new FileStream(Path.Combine(temporaryDirectory, $"dat-{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += read;
            if (total > LogiqxDatReader.MaximumBytes) throw new FormatException("DAT exceeds the 128 MiB upload limit.");
            hash.AppendData(buffer, 0, read);
            await spool.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        var sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        spool.Position = 0;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var existing = await database.DatFiles.SingleOrDefaultAsync(x => x.PlatformId == platformId && x.Sha256 == sha256, cancellationToken);
        if (existing is not null)
        {
            var games = await database.DatGames.CountAsync(x => x.DatFileId == existing.Id, cancellationToken);
            var roms = await database.DatRoms.CountAsync(x => x.DatGame.DatFileId == existing.Id, cancellationToken);
            return new DatImportResult(existing.Id, games, roms, true);
        }

        var dat = new DatFile { Name = safeName, FileName = safeName, PlatformId = platformId, Sha256 = sha256, ImportedAtUtc = DateTime.UtcNow };
        database.DatFiles.Add(dat);
        await database.SaveChangesAsync(cancellationToken);
        var gameCount = 0;
        var romCount = 0;
        await foreach (var record in reader.ReadAsync(spool, cancellationToken))
        {
            if (record is DatHeader header)
            {
                dat.Name = string.IsNullOrWhiteSpace(header.Name) ? safeName : header.Name;
                dat.Description = header.Description;
                dat.DatVersion = header.Version;
                dat.Author = header.Author;
                await database.SaveChangesAsync(cancellationToken);
            }
            else if (record is DatGameRecord entry)
            {
                entry.Game.DatFileId = dat.Id;
                database.DatGames.Add(entry.Game);
                gameCount++;
                romCount += entry.Game.Roms.Count;
                if (gameCount % 100 == 0)
                {
                    await database.SaveChangesAsync(cancellationToken);
                    database.ChangeTracker.Clear();
                }
            }
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Imported DAT {DatId} for platform {PlatformId}: {Games} games, {Roms} ROMs.", dat.Id, platformId, gameCount, romCount);
        return new DatImportResult(dat.Id, gameCount, romCount, false);
    }
}
