using System.Security.Cryptography;
using System.Text;
using Ludus.Application.Security;
using Ludus.Domain.Common;
using Ludus.Domain.Configuration;
using Ludus.Domain.Integrations;
using Ludus.Domain.Library;
using Ludus.Domain.Operations;
using Ludus.Domain.Profiles;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Imports;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Persistence;

public sealed class DatabaseSeeder(AppDbContext database, ISecretProtector secrets, LudusPaths paths)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        if (await database.AppSettings.AnyAsync(cancellationToken))
        {
            var security = await database.SecuritySettings.SingleAsync(cancellationToken);
            _ = secrets.Unprotect(security.ApiKey);
            var recoveryMarker = Path.Combine(paths.ConfigDirectory, "reset-account");
            if (File.Exists(recoveryMarker))
            {
                SafePaths.RejectLinks(recoveryMarker);
                File.Delete(recoveryMarker);
                security.Username = null;
                security.PasswordHash = null;
                security.AccountSetupMode = AccountSetupMode.LegacyProtected;
                security.SessionVersion = Guid.NewGuid();
                await CreateSetupTokenAsync(security, cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
            }
            else if (security.Username is not null && security.PasswordHash is not null)
            {
                security.AccountSetupMode = AccountSetupMode.Complete;
                security.SetupTokenHash = null;
                File.Delete(Path.Combine(paths.ConfigDirectory, "account-setup-token"));
                await database.SaveChangesAsync(cancellationToken);
            }
            else if (security.AccountSetupMode == AccountSetupMode.LegacyProtected && security.SetupTokenHash is null)
            {
                await CreateSetupTokenAsync(security, cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        var platforms = PlatformCatalog.Create();
        database.Platforms.AddRange(platforms);
        database.RootFolders.Add(new RootFolder { Name = "ROMs", Path = paths.RomDirectory });
        database.MetadataProviders.AddRange(Enum.GetValues<MetadataProviderKind>()
            .Select(provider => new MetadataProviderSettings { Provider = provider }));
        database.ScheduledTasks.AddRange(Enum.GetValues<TaskKind>().Select(kind => new ScheduledTask { Kind = kind }));
        database.AppSettings.Add(new AppSettings { Id = 1, RecyclingBinPath = paths.RecyclingDirectory });
        database.SecuritySettings.Add(new SecuritySettings
        {
            Id = 1,
            ApiKey = secrets.Protect(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))),
            SessionVersion = Guid.NewGuid()
        });

        var profile = new QualityProfile { Name = "Europe preferred", Platform = platforms[0], MinimumScore = 0, UpgradeUntilScore = 100 };
        AddFormat(profile, "Europe", FormatField.Region, "Europe", 100);
        AddFormat(profile, "USA", FormatField.Region, "USA", 80);
        AddFormat(profile, "CHD", FormatField.Extension, ".chd", 50);
        AddFormat(profile, "Beta", FormatField.Status, "Beta", -10000);
        AddFormat(profile, "Demo", FormatField.Status, "Demo", -10000);
        AddFormat(profile, "Prototype", FormatField.Status, "Proto", -10000);
        database.QualityProfiles.Add(profile);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task CreateSetupTokenAsync(SecuritySettings security, CancellationToken cancellationToken)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        security.SetupTokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var target = Path.Combine(paths.ConfigDirectory, "account-setup-token");
        SafePaths.RejectLinks(target);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var file = new FileStream(target, options))
        await using (var writer = new StreamWriter(file))
            await writer.WriteAsync(token.AsMemory(), cancellationToken);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void AddFormat(QualityProfile profile, string name, FormatField field, string value, int score)
    {
        var format = new CustomFormat
        {
            Name = name,
            Rules = [new CustomFormatRule { Field = field, Operator = FormatOperator.Equals, Value = value }]
        };
        profile.Formats.Add(new QualityProfileFormat { CustomFormat = format, Score = score });
    }
}
