using System.Security.Cryptography;
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
            DeleteObsoleteAccountArtifacts();
            if (security.Username is not null && security.PasswordHash is not null
                && security.AccountSetupMode != AccountSetupMode.Complete)
            {
                security.AccountSetupMode = AccountSetupMode.Complete;
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

    private void DeleteObsoleteAccountArtifacts()
    {
        File.Delete(Path.Combine(paths.ConfigDirectory, "account-setup-token"));
        File.Delete(Path.Combine(paths.ConfigDirectory, "reset-account"));
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
