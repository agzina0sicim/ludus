using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ludus.Application.Security;
using Ludus.Domain.Common;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Operations;

public sealed partial class DiagnosticsService(AppDbContext database, ISecretProtector secrets, LudusPaths paths)
{
    public async Task<IReadOnlyList<string>> LogsAsync(bool debug, string? level, int limit, CancellationToken cancellationToken)
    {
        var redact = await RedactorAsync(cancellationToken);
        var name = debug ? "ludus.debug.txt" : "ludus.txt";
        var path = SafePaths.Within(paths.LogsDirectory, name);
        if (!File.Exists(path)) return [];
        var lines = new Queue<string>();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 4 * 1024 * 1024) stream.Seek(-4 * 1024 * 1024, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        if (stream.Position > 0) await reader.ReadLineAsync(cancellationToken);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (level is not null && !line.Contains("[" + level + "]", StringComparison.OrdinalIgnoreCase)) continue;
            lines.Enqueue(redact(line.Length > 16384 ? line[..16384] : line));
            if (lines.Count > Math.Clamp(limit, 1, 2000)) lines.Dequeue();
        }
        return lines.ToArray();
    }

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken)
    {
        var normal = await LogsAsync(false, null, 2000, cancellationToken);
        var debug = await LogsAsync(true, null, 2000, cancellationToken);
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            await WriteAsync(zip, "logs/ludus.txt", string.Join('\n', normal), cancellationToken);
            await WriteAsync(zip, "logs/ludus.debug.txt", string.Join('\n', debug), cancellationToken);
            var status = new { version = typeof(DiagnosticsService).Assembly.GetName().Version!.ToString(), runtime = Environment.Version.ToString(),
                os = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Other", games = await database.Games.CountAsync(cancellationToken),
                files = await database.GameFiles.CountAsync(cancellationToken), schema = (await database.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault(),
                health = await database.HealthIssues.Select(x => new { x.Code, x.ResourceKey, x.Severity }).ToArrayAsync(cancellationToken) };
            await WriteAsync(zip, "system.json", JsonSerializer.Serialize(status), cancellationToken);
        }
        return memory.ToArray();
    }

    private async Task<Func<string, string>> RedactorAsync(CancellationToken cancellationToken)
    {
        var entities = new List<object>();
        entities.AddRange(await database.Indexers.AsNoTracking().ToArrayAsync(cancellationToken));
        entities.AddRange(await database.DownloadClients.AsNoTracking().ToArrayAsync(cancellationToken));
        entities.AddRange(await database.MetadataProviders.AsNoTracking().ToArrayAsync(cancellationToken));
        entities.AddRange(await database.Webhooks.AsNoTracking().ToArrayAsync(cancellationToken));
        entities.AddRange(await database.SecuritySettings.AsNoTracking().ToArrayAsync(cancellationToken));
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in entities)
        foreach (var property in entity.GetType().GetProperties().Where(x => x.PropertyType == typeof(ProtectedSecret)))
        {
            if (property.GetValue(entity) is not ProtectedSecret secret) continue;
            hidden.Add(secret.Ciphertext);
            try
            {
                var plaintext = secrets.Unprotect(secret);
                hidden.Add(plaintext); hidden.Add(Uri.EscapeDataString(plaintext)); hidden.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext)));
            }
            catch (CryptographicException) { }
        }
        var values = hidden.Where(x => x.Length > 0).OrderByDescending(x => x.Length).ToArray();
        return line =>
        {
            foreach (var value in values) line = line.Replace(value, "[REDACTED]", StringComparison.Ordinal);
            line = CredentialPattern().Replace(line, "$1[REDACTED]");
            line = UrlPattern().Replace(line, "[URL]");
            return line.Replace(paths.ConfigDirectory, "/config", StringComparison.OrdinalIgnoreCase)
                .Replace(paths.DownloadDirectory, "/downloads", StringComparison.OrdinalIgnoreCase).Replace(paths.RomDirectory, "/roms", StringComparison.OrdinalIgnoreCase);
        };
    }

    private static async Task WriteAsync(ZipArchive archive, string path, string content, CancellationToken cancellationToken)
    {
        await using var stream = archive.CreateEntry(path).Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken);
    }
    [GeneratedRegex(@"((?:api[-_]?key|access[-_]?token|password|client_secret|authorization|cookie|token)\s*[:=]\s*)[^\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex CredentialPattern();
    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex UrlPattern();
}
