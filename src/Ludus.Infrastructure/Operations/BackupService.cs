using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using Ludus.Application.Security;
using Ludus.Domain.Operations;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Downloads;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Operations;

public sealed record BackupManifest(int Format, string ApplicationVersion, string SchemaVersion, string DatabaseSha256, Dictionary<string, string> Keys);
public sealed record PendingRestore(string Directory, string ArchiveSha256);
public sealed class BackupGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public void Dispose() => Semaphore.Dispose();
}

public sealed class BackupService(IDbContextFactory<AppDbContext> factory, LudusPaths paths, BackupGate gate, DownloadGate downloads, ImportGate imports)
{
    public async Task<BackupRecord> ImportAsync(Stream content, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        string? target = null;
        try
        {
            var relative = $"manual/upload-{Guid.NewGuid():N}.zip";
            target = SafePaths.Within(Path.Combine(paths.ConfigDirectory, "Backups"), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var output = new FileStream(target, options))
            {
                var buffer = new byte[131072]; long total = 0; int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if ((total += read) > 9L * 1024 * 1024 * 1024) throw new InvalidDataException("Backup upload too large.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            BackupManifest manifest;
            using (var archive = ZipFile.OpenRead(target))
            {
                if (archive.Entries.Count > 4096) throw new InvalidDataException("Too many backup entries.");
                var entry = archive.Entries.SingleOrDefault(x => x.FullName == "manifest.json") ?? throw new InvalidDataException("Backup manifest missing.");
                if (entry.Length > 1024 * 1024) throw new InvalidDataException("Backup manifest too large.");
                await using var stream = entry.Open();
                using var bounded = new MemoryStream();
                var manifestBuffer = new byte[16384]; int count;
                while ((count = await stream.ReadAsync(manifestBuffer, cancellationToken)) > 0)
                {
                    if (bounded.Length + count > 1024 * 1024) throw new InvalidDataException("Backup manifest too large.");
                    bounded.Write(manifestBuffer, 0, count);
                }
                manifest = JsonSerializer.Deserialize<BackupManifest>(bounded.ToArray()) ?? throw new InvalidDataException("Invalid backup manifest.");
            }
            await using var database = await factory.CreateDbContextAsync(cancellationToken);
            if (manifest.Format != 1 || string.IsNullOrWhiteSpace(manifest.ApplicationVersion) || manifest.ApplicationVersion.Length > 128
                || manifest.Keys is null || !database.Database.GetMigrations().Contains(manifest.SchemaVersion)) throw new InvalidDataException("Unsupported backup format.");
            var record = new BackupRecord { RelativePath = relative, ApplicationVersion = manifest.ApplicationVersion, SchemaVersion = manifest.SchemaVersion,
                Size = new FileInfo(target).Length, Sha256 = await HashAsync(target, cancellationToken), Scheduled = false };
            database.Backups.Add(record); await database.SaveChangesAsync(cancellationToken);
            target = null;
            return record;
        }
        finally
        {
            if (target is not null && File.Exists(target)) File.Delete(target);
            gate.Semaphore.Release();
        }
    }

    public async Task<BackupRecord> CreateAsync(bool scheduled, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            await downloads.Semaphore.WaitAsync(cancellationToken);
            try
            {
                await imports.Semaphore.WaitAsync(cancellationToken);
                try { return await CreateCoreAsync(scheduled, cancellationToken); }
                finally { imports.Semaphore.Release(); }
            }
            finally { downloads.Semaphore.Release(); }
        }
        finally { gate.Semaphore.Release(); }
    }

    private async Task<BackupRecord> CreateCoreAsync(bool scheduled, CancellationToken cancellationToken)
    {
        var journals = Path.Combine(paths.ConfigDirectory, "Imports");
        if (Directory.Exists(journals) && Directory.EnumerateFiles(journals, "*.json").Any())
            throw new InvalidOperationException("Pending imports must be recovered before a consistent backup can be created.");
        var stage = SafePaths.Within(paths.ConfigDirectory, "backup-work/" + Guid.NewGuid().ToString("N"));
        PrivateDirectory(stage);
        try
        {
            await using var database = await factory.CreateDbContextAsync(cancellationToken);
            var snapshot = Path.Combine(stage, "ludus.db");
            await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
            await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot, Pooling = false }.ToString()))
            {
                await source.OpenAsync(cancellationToken);
                await destination.OpenAsync(cancellationToken);
                source.BackupDatabase(destination);
            }
            var keyHashes = new Dictionary<string, string>();
            foreach (var key in Directory.EnumerateFiles(paths.KeysDirectory, "*.xml"))
            {
                SafePaths.RejectLinks(key);
                var name = "keys/" + Path.GetFileName(key);
                var target = SafePaths.Within(stage, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(key, target);
                keyHashes[name] = await HashAsync(target, cancellationToken);
            }
            var schema = (await database.Database.GetAppliedMigrationsAsync(cancellationToken)).Last();
            var manifest = new BackupManifest(1, typeof(BackupService).Assembly.GetName().Version!.ToString(), schema, await HashAsync(snapshot, cancellationToken), keyHashes);
            await AtomicFiles.WriteJsonAsync(Path.Combine(stage, "manifest.json"), manifest, cancellationToken);
            var relative = $"{(scheduled ? "scheduled" : "manual")}/ludus-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip";
            var targetZip = SafePaths.Within(Path.Combine(paths.ConfigDirectory, "Backups"), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetZip)!);
            var zipOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) zipOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var zipStream = new FileStream(targetZip, zipOptions))
                ZipFile.CreateFromDirectory(stage, zipStream, CompressionLevel.Optimal, false);
            ProtectFile(targetZip);
            var record = new BackupRecord { RelativePath = relative, ApplicationVersion = manifest.ApplicationVersion, SchemaVersion = schema,
                Sha256 = await HashAsync(targetZip, cancellationToken), Size = new FileInfo(targetZip).Length, Scheduled = scheduled };
            database.Backups.Add(record);
            await database.SaveChangesAsync(cancellationToken);
            if (scheduled)
            {
                var keep = (await database.AppSettings.SingleAsync(cancellationToken)).BackupRetentionCount;
                var expired = await database.Backups.Where(x => x.Scheduled).OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Skip(keep).ToArrayAsync(cancellationToken);
                foreach (var old in expired) File.Delete(SafePaths.Within(Path.Combine(paths.ConfigDirectory, "Backups"), old.RelativePath));
                database.Backups.RemoveRange(expired);
                await database.SaveChangesAsync(cancellationToken);
            }
            return record;
        }
        finally { SafePaths.DeleteOwnedTree(Path.Combine(paths.ConfigDirectory, "backup-work"), stage); }
    }

    public async Task StageRestoreAsync(long backupId, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(Path.Combine(paths.ConfigDirectory, "restore-pending.json"))) throw new InvalidOperationException("A restore is already pending.");
            await using var database = await factory.CreateDbContextAsync(cancellationToken);
            var backup = await database.Backups.AsNoTracking().SingleOrDefaultAsync(x => x.Id == backupId, cancellationToken) ?? throw new KeyNotFoundException();
            var zip = SafePaths.Within(Path.Combine(paths.ConfigDirectory, "Backups"), backup.RelativePath);
            if (await HashAsync(zip, cancellationToken) != backup.Sha256) throw new InvalidDataException("Backup checksum mismatch.");
            var stage = SafePaths.Within(paths.ConfigDirectory, "restore-work/" + Guid.NewGuid().ToString("N"));
            PrivateDirectory(stage);
            try
            {
                using var archive = ZipFile.OpenRead(zip);
                if (archive.Entries.Count > 4096) throw new InvalidDataException("Too many backup entries.");
                long total = 0;
                foreach (var entry in archive.Entries)
                {
                    var name = entry.FullName;
                    if (name != "ludus.db" && name != "manifest.json" && !(name.StartsWith("keys/", StringComparison.Ordinal) && name.EndsWith(".xml", StringComparison.Ordinal) && name.Count(c => c == '/') == 1))
                        throw new InvalidDataException("Unexpected backup entry.");
                    var target = SafePaths.Within(stage, name);
                    var limit = name == "ludus.db" ? 8L * 1024 * 1024 * 1024 : 1024 * 1024;
                    if (entry.Length > limit || (total += entry.Length) > 9L * 1024 * 1024 * 1024) throw new InvalidDataException("Backup size limit exceeded.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var input = entry.Open();
                    await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var buffer = new byte[131072]; long written = 0; int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        if ((written += read) > limit || written > entry.Length) throw new InvalidDataException("Backup expansion limit exceeded.");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                    if (written != entry.Length) throw new InvalidDataException("Truncated backup.");
                }
                await ValidateAsync(stage, backup.SchemaVersion, cancellationToken);
                await AtomicFiles.WriteJsonAsync(Path.Combine(paths.ConfigDirectory, "restore-pending.json"), new PendingRestore(stage, backup.Sha256), cancellationToken);
            }
            catch { SafePaths.DeleteOwnedTree(Path.Combine(paths.ConfigDirectory, "restore-work"), stage); throw; }
        }
        finally { gate.Semaphore.Release(); }
    }

    public static async Task ApplyPendingAsync(LudusPaths paths, CancellationToken cancellationToken)
    {
        var marker = Path.Combine(paths.ConfigDirectory, "restore-pending.json");
        if (!File.Exists(marker)) return;
        SafePaths.RejectLinks(marker);
        var pending = JsonSerializer.Deserialize<PendingRestore>(await File.ReadAllTextAsync(marker, cancellationToken)) ?? throw new InvalidDataException("Invalid restore marker.");
        if (!SafePaths.IsWithin(Path.Combine(paths.ConfigDirectory, "restore-work"), pending.Directory)) throw new InvalidDataException("Invalid restore directory.");
        var manifest = await ValidateAsync(pending.Directory, null, cancellationToken);
        var rollback = SafePaths.Within(paths.ConfigDirectory, "restore-previous/" + Path.GetFileName(pending.Directory));
        PrivateDirectory(rollback);
        SqliteConnection.ClearAllPools();
        var journals = SafePaths.Within(paths.ConfigDirectory, "Imports");
        var previousJournals = SafePaths.Within(rollback, "Imports");
        if (Directory.Exists(journals) && !Directory.Exists(previousJournals))
        {
            SafePaths.EnumerateFiles(journals, 1000000);
            Directory.Move(journals, previousJournals);
        }
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var current = paths.DatabasePath + suffix;
            var old = Path.Combine(rollback, "ludus.db" + suffix);
            if (File.Exists(current) && !File.Exists(old)) File.Copy(current, old);
        }
        foreach (var key in manifest.Keys.Keys)
        {
            var source = SafePaths.Within(pending.Directory, key);
            var target = SafePaths.Within(paths.ConfigDirectory, key);
            if (File.Exists(target) && await HashAsync(target, cancellationToken) != manifest.Keys[key]) throw new InvalidDataException("Data protection key collision.");
            if (!File.Exists(target)) File.Copy(source, target);
            ProtectFile(target);
        }
        var incoming = paths.DatabasePath + ".restore";
        File.Copy(Path.Combine(pending.Directory, "ludus.db"), incoming, true);
        File.Delete(paths.DatabasePath + "-wal"); File.Delete(paths.DatabasePath + "-shm");
        File.Move(incoming, paths.DatabasePath, true);
        ProtectFile(paths.DatabasePath);
        await using (var restored = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = false }.ToString()))
        {
            await restored.OpenAsync(cancellationToken);
            using var invalidate = restored.CreateCommand();
            invalidate.CommandText = "UPDATE SecuritySettings SET SessionVersion = $session, Version = $version WHERE Id = 1";
            invalidate.Parameters.AddWithValue("$session", Guid.NewGuid().ToString().ToUpperInvariant());
            invalidate.Parameters.AddWithValue("$version", Guid.NewGuid().ToString().ToUpperInvariant());
            await invalidate.ExecuteNonQueryAsync(cancellationToken);
        }
        File.Delete(marker);
        SafePaths.DeleteOwnedTree(Path.Combine(paths.ConfigDirectory, "restore-work"), pending.Directory);
    }

    private static async Task<BackupManifest> ValidateAsync(string directory, string? expectedSchema, CancellationToken cancellationToken)
    {
        SafePaths.RejectLinks(directory);
        var manifestPath = SafePaths.Within(directory, "manifest.json");
        if (new FileInfo(manifestPath).Length > 1024 * 1024) throw new InvalidDataException("Invalid manifest size.");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken)) ?? throw new InvalidDataException("Invalid backup manifest.");
        if (manifest.Format != 1 || manifest.Keys is null || manifest.Keys.Count == 0 || expectedSchema is not null && manifest.SchemaVersion != expectedSchema) throw new InvalidDataException("Invalid backup format.");
        var actualKeys = Directory.GetFiles(Path.Combine(directory, "keys"), "*", SearchOption.AllDirectories)
            .Select(x => Path.GetRelativePath(directory, x).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        if (!actualKeys.SetEquals(manifest.Keys.Keys)) throw new InvalidDataException("Unexpected backup keys.");
        var snapshot = SafePaths.Within(directory, "ludus.db");
        if (await HashAsync(snapshot, cancellationToken) != manifest.DatabaseSha256) throw new InvalidDataException("Database checksum mismatch.");
        foreach (var key in manifest.Keys)
        {
            if (!key.Key.StartsWith("keys/", StringComparison.Ordinal) || key.Key.Count(c => c == '/') != 1 || !key.Key.EndsWith(".xml", StringComparison.Ordinal)) throw new InvalidDataException("Invalid backup key path.");
            var path = SafePaths.Within(directory, key.Key);
            if (await HashAsync(path, cancellationToken) != key.Value) throw new InvalidDataException("Key checksum mismatch.");
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.LocalName == "encryptedSecret") throw new InvalidDataException("Machine-encrypted keyrings are not portable backups.");
                var descriptor = reader.GetAttribute("deserializerType");
                if (descriptor is not null && !descriptor.StartsWith("Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel.AuthenticatedEncryptorDescriptorDeserializer, Microsoft.AspNetCore.DataProtection,", StringComparison.Ordinal))
                    throw new InvalidDataException("Unsupported key descriptor type.");
                if (reader.GetAttribute("decryptorType") is not null) throw new InvalidDataException("Custom key decryptors are not supported.");
            }
        }
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check;";
        if (!Equals(await command.ExecuteScalarAsync(cancellationToken), "ok")) throw new InvalidDataException("SQLite integrity check failed.");
        command.CommandText = "PRAGMA foreign_key_check;";
        if (await command.ExecuteScalarAsync(cancellationToken) is not null) throw new InvalidDataException("SQLite foreign key check failed.");
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1";
        if (!Equals(await command.ExecuteScalarAsync(cancellationToken), manifest.SchemaVersion)) throw new InvalidDataException("Schema mismatch.");
        var options = new DbContextOptionsBuilder<AppDbContext>(); DatabaseOptions.Configure(options, snapshot);
        await using (var model = new AppDbContext(options.Options))
            if (!model.Database.GetMigrations().Contains(manifest.SchemaVersion)) throw new InvalidDataException("Unsupported backup schema.");
        command.CommandText = "SELECT ApiKey FROM SecuritySettings WHERE Id = 1";
        var cipher = (string?)await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidDataException("Backup security settings missing.");
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys")), builder => builder.SetApplicationName("Ludus"));
        var protector = new Security.DataProtectionSecretProtector(protection);
        if (protector.Unprotect(new Domain.Common.ProtectedSecret(cipher)).Length != 64) throw new InvalidDataException("Backup keyring cannot decrypt configuration.");
        return manifest;
    }

    public static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        SafePaths.RejectLinks(path);
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }
    private static void ProtectFile(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    private static void PrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
