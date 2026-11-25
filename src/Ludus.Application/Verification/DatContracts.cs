using Ludus.Domain.Common;

namespace Ludus.Application.Verification;

public sealed record FileHashes(long Size, string Crc32, string Md5, string Sha1);
public sealed record DatImportResult(long DatFileId, int Games, int Roms, bool AlreadyImported);
public sealed record DatMatch(long DatRomId, string CanonicalName, string RomName);
public sealed record VerificationResult(VerificationStatus Status, string ReasonCode, IReadOnlyList<DatMatch> Matches);

public interface IFileHasher
{
    Task<FileHashes> ComputeAsync(Stream stream, CancellationToken cancellationToken = default);
}

public interface IDatImporter
{
    Task<DatImportResult> ImportAsync(long platformId, string fileName, Stream content, CancellationToken cancellationToken = default);
}

public interface IDatVerifier
{
    Task<VerificationResult> VerifyAsync(long platformId, FileHashes hashes, string? expectedCanonicalName = null,
        CancellationToken cancellationToken = default);
}
