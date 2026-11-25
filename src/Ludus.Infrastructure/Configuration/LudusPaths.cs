using Microsoft.Extensions.Configuration;

namespace Ludus.Infrastructure.Configuration;

public sealed record LudusPaths(string ConfigDirectory, string DownloadDirectory, string RomDirectory)
{
    public string DatabasePath => Path.Combine(ConfigDirectory, "ludus.db");
    public string KeysDirectory => Path.Combine(ConfigDirectory, "keys");
    public string LogsDirectory => Path.Combine(ConfigDirectory, "logs");
    public string RecyclingDirectory => Path.Combine(ConfigDirectory, "RecyclingBin");
    public string BackupDirectory => Path.Combine(ConfigDirectory, "Backups", "scheduled");
    public string MediaDirectory => Path.Combine(ConfigDirectory, "Media");

    public static LudusPaths FromConfiguration(IConfiguration configuration)
    {
        var localRoot = Path.Combine(Directory.GetCurrentDirectory(), ".local");
        return new LudusPaths(
            Resolve(configuration["Ludus:ConfigDirectory"], OperatingSystem.IsWindows() ? Path.Combine(localRoot, "config") : "/config"),
            Resolve(configuration["Ludus:DownloadDirectory"], OperatingSystem.IsWindows() ? Path.Combine(localRoot, "downloads") : "/downloads"),
            Resolve(configuration["Ludus:RomDirectory"], OperatingSystem.IsWindows() ? Path.Combine(localRoot, "roms") : "/roms"));
    }

    public void EnsureConfigDirectories()
    {
        foreach (var directory in new[] { ConfigDirectory, KeysDirectory, LogsDirectory, RecyclingDirectory, BackupDirectory, MediaDirectory })
        {
            Directory.CreateDirectory(directory);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(KeysDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static string Resolve(string? configured, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        if (!Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException("Ludus storage directories must be absolute paths.");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }
}
