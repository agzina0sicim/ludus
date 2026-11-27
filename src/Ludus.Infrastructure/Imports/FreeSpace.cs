namespace Ludus.Infrastructure.Imports;

public static class FreeSpace
{
    public static void EnsureAvailable(string targetPath, long bytesToWrite, long minimumFreeSpaceBytes, bool skip)
    {
        if (skip) return;
        if (bytesToWrite < 0 || minimumFreeSpaceBytes < 0) throw new ArgumentOutOfRangeException(nameof(bytesToWrite));
        var full = Path.GetFullPath(targetPath);
        var drive = OperatingSystem.IsWindows()
            ? new DriveInfo(Path.GetPathRoot(full) ?? throw new ImportException("TargetDriveUnavailable", false))
            : DriveInfo.GetDrives().Where(x => x.IsReady && (full.Equals(Path.TrimEndingDirectorySeparator(x.RootDirectory.FullName),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || full.StartsWith(Path.TrimEndingDirectorySeparator(x.RootDirectory.FullName) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            .OrderByDescending(x => x.RootDirectory.FullName.Length).FirstOrDefault()
            ?? throw new ImportException("TargetDriveUnavailable", false);
        long required;
        try { required = checked(bytesToWrite + minimumFreeSpaceBytes); }
        catch (OverflowException) { throw new ImportException("InsufficientFreeSpace", false); }
        if (drive.AvailableFreeSpace < required) throw new ImportException("InsufficientFreeSpace", false);
    }
}
