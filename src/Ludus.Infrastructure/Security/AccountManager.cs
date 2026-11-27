using Ludus.Domain.Common;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Security;

public sealed class AccountManager(AppDbContext database)
{
    public async Task<Guid?> SetCredentialsAsync(string username, string password,
        AccountSetupMode? requiredSetupMode, CancellationToken cancellationToken)
    {
        var normalizedUsername = username.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUsername);
        if (normalizedUsername.Length > 128)
            throw new ArgumentException("The username must not exceed 128 characters.", nameof(username));
        if (password.Length is < 12 or > 1024)
            throw new ArgumentException("The password must contain between 12 and 1024 characters.", nameof(password));

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var settings = await database.SecuritySettings.SingleAsync(cancellationToken);
        if (requiredSetupMode is not null && settings.AccountSetupMode != requiredSetupMode)
            return null;
        settings.Username = normalizedUsername;
        settings.PasswordHash = new PasswordHasher<string>().HashPassword(normalizedUsername, password);
        settings.AuthenticationMethod = AuthenticationMethod.Forms;
        settings.AccountSetupMode = AccountSetupMode.Complete;
        settings.SessionVersion = Guid.NewGuid();
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return settings.SessionVersion;
    }
}
