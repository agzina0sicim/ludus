using System.Runtime.InteropServices;
using Ludus.Domain.Configuration;

namespace Ludus.Infrastructure.Imports;

public static class MediaPermissions
{
    public static bool Supported => OperatingSystem.IsLinux();

    public static void CreateDirectory(string path, AppSettings settings)
    {
        if (Directory.Exists(path)) return;
        var missing = new Stack<string>();
        for (var current = Path.GetFullPath(path); !Directory.Exists(current); current = Directory.GetParent(current)?.FullName
            ?? throw new DirectoryNotFoundException(path))
            missing.Push(current);
        Directory.CreateDirectory(path);
        foreach (var directory in missing) ApplyDirectory(directory, settings);
    }

    public static void ApplyDirectory(string path, AppSettings settings)
    {
        if (!OperatingSystem.IsLinux()) return;
        File.SetUnixFileMode(path, Parse(settings.DirectoryPermissions));
        ApplyGroup(path, settings.MediaGroup);
    }

    public static void ApplyFile(string path, AppSettings settings, bool hardlinked)
    {
        if (!OperatingSystem.IsLinux() || hardlinked) return;
        File.SetUnixFileMode(path, Parse(settings.FilePermissions));
        ApplyGroup(path, settings.MediaGroup);
    }

    private static UnixFileMode Parse(string value) => (UnixFileMode)Convert.ToInt32(value, 8);

    private static void ApplyGroup(string path, string? group)
    {
        if (string.IsNullOrWhiteSpace(group)) return;
        var pointer = GetGroup(group);
        if (pointer == IntPtr.Zero) throw new ImportException("MediaGroupNotFound", false);
        var entry = Marshal.PtrToStructure<GroupEntry>(pointer);
        if (Chown(path, uint.MaxValue, entry.GroupId) != 0) throw new UnauthorizedAccessException("Configured media group could not be applied.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GroupEntry
    {
        public IntPtr Name;
        public IntPtr Password;
        public uint GroupId;
        public IntPtr Members;
    }

    [DllImport("libc", EntryPoint = "getgrnam", SetLastError = true)]
    private static extern IntPtr GetGroup([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("libc", EntryPoint = "chown", SetLastError = true)]
    private static extern int Chown([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint owner, uint group);
}
