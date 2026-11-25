using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using Ludus.Application.Verification;

namespace Ludus.Infrastructure.Verification;

public sealed class FileHasher : IFileHasher
{
    private static readonly uint[] CrcTable = CreateCrcTable();

    public async Task<FileHashes> ComputeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        var crc = uint.MaxValue;
        long size = 0;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
            {
                size = checked(size + read);
                md5.AppendData(buffer, 0, read);
                sha1.AppendData(buffer, 0, read);
                for (var i = 0; i < read; i++) crc = CrcTable[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
            }
            return new FileHashes(size, (~crc).ToString("x8", CultureInfo.InvariantCulture),
                Convert.ToHexStringLower(md5.GetHashAndReset()), Convert.ToHexStringLower(sha1.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var crc = i;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) == 1 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1;
            table[i] = crc;
        }
        return table;
    }
}
