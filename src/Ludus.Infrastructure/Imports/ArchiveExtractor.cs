using System.Globalization;
using Ludus.Application.Verification;
using Ludus.Domain.Configuration;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Ludus.Infrastructure.Imports;

public sealed class ArchiveExtractor(IFileHasher hasher)
{
    public async Task ExtractAsync(string archivePath, string destination, AppSettings settings, CancellationToken cancellationToken)
    {
        SafePaths.RejectLinks(archivePath);
        SafePaths.RejectLinks(destination);
        MediaPermissions.CreateDirectory(destination, settings);
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            if (archive.Type is not (ArchiveType.Zip or ArchiveType.SevenZip)) throw new ImportException("UnsupportedArchive", true);
            var existing = SafePaths.EnumerateFiles(destination, settings.MaximumArchiveEntries);
            long total = existing.Sum(path => new FileInfo(path).Length);
            var count = existing.Count;
            var buffer = new byte[131072];
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > settings.MaximumArchiveEntries) throw new ImportException("ArchiveEntryLimit", true);
                if (entry.IsEncrypted || entry.LinkTarget is not null) throw new ImportException("EncryptedOrLinkedArchive", true);
                var key = entry.Key?.TrimEnd('/', '\\') ?? "";
                var target = SafePaths.Within(destination, key);
                if (entry.IsDirectory) { MediaPermissions.CreateDirectory(target, settings); continue; }
                if (entry.Size < 0 || entry.Size > settings.MaximumExtractedBytes - total) throw new ImportException("ArchiveSizeLimit", true);
                FreeSpace.EnsureAvailable(target, entry.Size, settings.MinimumFreeSpaceBytes, settings.SkipFreeSpaceCheck);
                MediaPermissions.CreateDirectory(Path.GetDirectoryName(target)!, settings);
                using var input = entry.OpenEntryStream();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous);
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += read;
                    written += read;
                    if (total > settings.MaximumExtractedBytes) throw new ImportException("ArchiveSizeLimit", true);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (written != entry.Size) throw new ImportException("TruncatedArchive", true);
                await output.DisposeAsync();
                if (archive.Type == ArchiveType.Zip || entry.Crc != 0)
                {
                    await using var verified = File.OpenRead(target);
                    var hashes = await hasher.ComputeAsync(verified, cancellationToken);
                    if (uint.Parse(hashes.Crc32, NumberStyles.HexNumber, CultureInfo.InvariantCulture) != unchecked((uint)entry.Crc))
                        throw new ImportException("ArchiveChecksumMismatch", true);
                }
            }
            if (count == 0) throw new ImportException("EmptyArchive", true);
        }
        catch (OperationCanceledException) { throw; }
        catch (ImportException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch (EndOfStreamException) { throw new ImportException("TruncatedArchive", true); }
        catch (IOException) { throw; }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException
            || exception.GetType().Namespace?.StartsWith("SharpCompress", StringComparison.Ordinal) == true)
        {
            throw new ImportException("InvalidArchive", true);
        }
    }
}
