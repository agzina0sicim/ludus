using System.Runtime.InteropServices;
using System.Text.Json;

namespace Ludus.Infrastructure.Imports;

public sealed class AtomicFiles
{
    public async Task<bool> StageAsync(string source, string target, bool preferHardlinks, CancellationToken cancellationToken,
        bool skipFreeSpaceCheck = true, long minimumFreeSpaceBytes = 0)
    {
        SafePaths.RejectLinks(source);
        SafePaths.RejectLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target)) throw new ImportException("DestinationExists", false);
        if (preferHardlinks && TryHardLink(source, target)) return true;
        FreeSpace.EnsureAvailable(target, new FileInfo(source).Length, minimumFreeSpaceBytes, skipFreeSpaceCheck);
        await CopyAsync(source, target, cancellationToken);
        return false;
    }

    public async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        SafePaths.RejectLinks(source);
        SafePaths.RejectLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(true);
            }
            File.Move(temporary, target, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporary = path + ".new";
        SafePaths.RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    public static bool TryHardLink(string source, string target)
    {
        if (OperatingSystem.IsWindows()) return CreateHardLinkWindows(target, source, IntPtr.Zero);
        if (OperatingSystem.IsLinux()) return CreateHardLinkUnix(source, target) == 0;
        return false;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(string newFile, string existingFile, IntPtr securityAttributes);
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateHardLinkUnix([MarshalAs(UnmanagedType.LPUTF8Str)] string existingFile, [MarshalAs(UnmanagedType.LPUTF8Str)] string newFile);
}
