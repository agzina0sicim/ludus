using Ludus.Application.Verification;
using Ludus.Domain.Common;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Verification;

public sealed class DatVerifier(IDbContextFactory<AppDbContext> databaseFactory) : IDatVerifier
{
    public async Task<VerificationResult> VerifyAsync(long platformId, FileHashes hashes, string? expectedCanonicalName = null,
        CancellationToken cancellationToken = default)
    {
        if (hashes.Size < 0 || !ValidHash(hashes.Crc32, 8) || !ValidHash(hashes.Md5, 32) || !ValidHash(hashes.Sha1, 40))
            throw new ArgumentException("Invalid calculated file hashes.", nameof(hashes));
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var catalog = database.DatRoms.AsNoTracking().Where(x => x.DatGame.DatFile.PlatformId == platformId && x.DatGame.DatFile.Enabled);
        var candidates = await catalog.Where(x => x.Sha1 == hashes.Sha1 || x.Md5 == hashes.Md5 || x.Crc32 == hashes.Crc32)
            .Select(x => new { x.Id, x.Name, CanonicalName = x.DatGame.Name, x.Size, x.Sha1, x.Md5, x.Crc32, x.Status })
            .ToListAsync(cancellationToken);
        var exact = candidates.Where(x => (x.Size is null || x.Size == hashes.Size)
            && Matches(x.Sha1, hashes.Sha1) && Matches(x.Md5, hashes.Md5) && Matches(x.Crc32, hashes.Crc32)).ToArray();
        var good = exact.Where(x => x.Status is null or "good" or "verified" && (x.Sha1 is not null || x.Md5 is not null))
            .Select(x => new DatMatch(x.Id, x.CanonicalName, x.Name)).OrderBy(x => x.DatRomId).ToArray();
        if (good.Length != 0) return new VerificationResult(VerificationStatus.VerifiedGoodDump, "VerifiedGoodDump", good);
        if (exact.Any(x => x.Status == "baddump")) return new VerificationResult(VerificationStatus.Modified, "KnownBadDump", []);
        if (exact.Length != 0) return new VerificationResult(VerificationStatus.Unverified, "InsufficientTrustedChecksums", []);
        var knownName = expectedCanonicalName is not null && await catalog.AnyAsync(x => x.DatGame.Name == expectedCanonicalName, cancellationToken);
        return candidates.Count > 0 || knownName
            ? new VerificationResult(VerificationStatus.Modified, "ChecksumMismatch", [])
            : new VerificationResult(VerificationStatus.Unverified, "NoDatMatch", []);
    }

    private static bool Matches(string? expected, string actual) => expected is null || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    private static bool ValidHash(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);
}
