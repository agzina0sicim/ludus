namespace Ludus.Infrastructure.Imports;

public static class SafePaths
{
    public static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Within(string root, string relative)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ImportException("RootMustBeAbsolute", false);
        var segments = relative.Replace('\\', '/').Split('/');
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') || segments.Any(x => x is "" or "." or ".."
            || x.Contains(':') || x.Any(char.IsControl) || x.Any(c => "<>\"|?*".Contains(c)) || x.TrimEnd(' ', '.') != x)) throw new ImportException("UnsafePath", true);
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        if (!IsWithin(root, path)) throw new ImportException("UnsafePath", true);
        RejectLinks(path);
        return path;
    }

    public static bool IsWithin(string root, string path)
    {
        var absoluteRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var absolutePath = Path.GetFullPath(path);
        return absolutePath.StartsWith(absoluteRoot + Path.DirectorySeparatorChar, Comparison);
    }

    public static void RejectLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ImportException("SymbolicLinksNotAllowed", true);
            current = Path.GetDirectoryName(current);
        }
    }

    public static IReadOnlyList<string> EnumerateFiles(string directory, int maximumEntries)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);
        var entries = 0;
        while (pending.TryPop(out var folder))
        {
            RejectLinks(folder);
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                if (++entries > maximumEntries) throw new ImportException("TooManyFiles", true);
                RejectLinks(entry);
                if (Directory.Exists(entry)) pending.Push(entry);
                else files.Add(entry);
            }
        }
        return files;
    }

    public static string Component(string value)
    {
        var result = new string(value.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (result.Length == 0) result = "Unknown";
        var stem = result.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))) result = "_" + result;
        return result.Length > 160 ? result[..160] : result;
    }

    public static void DeleteOwnedTree(string root, string path)
    {
        if (!IsWithin(root, path)) throw new ImportException("UnsafeCleanup", false);
        if (!Directory.Exists(path)) return;
        EnumerateFiles(path, 1000000);
        Directory.Delete(Path.GetFullPath(path), true);
    }
}
