using Ludus.Api;
using Ludus.Domain.Common;
using Ludus.Infrastructure;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ludus.Infrastructure.Tests;

public sealed class AccountCommandTests
{
    [Fact]
    public async Task AccountSetConfiguresLegacyInstallAndCanRecoverExistingAccount()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Ludus.AccountCommand.Tests", Guid.NewGuid().ToString("N"));
        var paths = new LudusPaths(Path.Combine(directory, "config"), Path.Combine(directory, "downloads"), Path.Combine(directory, "roms"));
        paths.EnsureConfigDirectories();
        try
        {
            await using var services = BuildServices(paths);
            await using (var scope = services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
                var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var settings = await database.SecuritySettings.SingleAsync();
                settings.AccountSetupMode = AccountSetupMode.LegacyProtected;
                await database.SaveChangesAsync();
            }

            await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "account-setup-token"), "obsolete-secret");
            await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "reset-account"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "bootstrap-api-key"), "obsolete-bootstrap-key");

            Guid originalSession;
            string originalApiKey;
            await using (var scope = services.CreateAsyncScope())
            {
                var settings = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SecuritySettings.AsNoTracking().SingleAsync();
                originalSession = settings.SessionVersion;
                originalApiKey = settings.ApiKey.Ciphertext;
            }

            var firstConsole = new FakeAccountConsole("local-admin", "new local password 2026", "new local password 2026");
            Assert.Equal(0, await AccountCommand.RunAsync(services, paths, firstConsole));
            Assert.DoesNotContain("new local password 2026", firstConsole.Output, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(paths.ConfigDirectory, "account-setup-token")));
            Assert.False(File.Exists(Path.Combine(paths.ConfigDirectory, "reset-account")));
            Assert.False(File.Exists(Path.Combine(paths.ConfigDirectory, "bootstrap-api-key")));

            Guid configuredSession;
            await using (var scope = services.CreateAsyncScope())
            {
                var settings = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SecuritySettings.AsNoTracking().SingleAsync();
                configuredSession = settings.SessionVersion;
                Assert.Equal(AccountSetupMode.Complete, settings.AccountSetupMode);
                Assert.Equal(AuthenticationMethod.Forms, settings.AuthenticationMethod);
                Assert.Equal("local-admin", settings.Username);
                Assert.NotEqual(originalSession, configuredSession);
                Assert.Equal(originalApiKey, settings.ApiKey.Ciphertext);
                Assert.Equal(PasswordVerificationResult.Success,
                    new PasswordHasher<string>().VerifyHashedPassword(settings.Username!, settings.PasswordHash!, "new local password 2026"));
            }

            var recoveryConsole = new FakeAccountConsole("recovered-admin", "recovered password 2026", "recovered password 2026");
            Assert.Equal(0, await AccountCommand.RunAsync(services, paths, recoveryConsole));
            await using (var scope = services.CreateAsyncScope())
            {
                var settings = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SecuritySettings.AsNoTracking().SingleAsync();
                Assert.Equal("recovered-admin", settings.Username);
                Assert.NotEqual(configuredSession, settings.SessionVersion);
                Assert.Equal(PasswordVerificationResult.Success,
                    new PasswordHasher<string>().VerifyHashedPassword(settings.Username!, settings.PasswordHash!, "recovered password 2026"));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AccountSetRefusesToRunWhileInstanceLockIsHeld()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Ludus.AccountCommand.Tests", Guid.NewGuid().ToString("N"));
        var paths = new LudusPaths(Path.Combine(directory, "config"), Path.Combine(directory, "downloads"), Path.Combine(directory, "roms"));
        paths.EnsureConfigDirectories();
        try
        {
            await using var services = BuildServices(paths);
            using var runningInstance = new InstanceLock(paths);
            var console = new FakeAccountConsole("admin", "correct horse battery", "correct horse battery");
            Assert.Equal(1, await AccountCommand.RunAsync(services, paths, console));
            Assert.Contains("Stoppe den Ludus-Dienst", console.Output, StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceProvider BuildServices(LudusPaths paths) => new ServiceCollection()
        .AddLogging()
        .AddInfrastructure(paths)
        .AddSingleton<InstanceLock>()
        .BuildServiceProvider();

    private sealed class FakeAccountConsole(string username, params string[] secrets) : IAccountConsole
    {
        private readonly Queue<string> _secrets = new(secrets);
        private readonly StringWriter _output = new();
        public bool IsInteractive => true;
        public string Output => _output.ToString();
        public string? ReadLine() => username;
        public string ReadSecret() => _secrets.Dequeue();
        public void Write(string value) => _output.Write(value);
        public void WriteLine(string value) => _output.WriteLine(value);
    }
}
