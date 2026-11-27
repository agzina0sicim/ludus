using Ludus.Domain.Common;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Persistence;
using Ludus.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Ludus.Api;

public static class AccountCommand
{
    public static bool IsCommand(string[] arguments) =>
        arguments.Length > 0 && string.Equals(arguments[0], "account", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IServiceProvider services, LudusPaths paths,
        IAccountConsole? console = null, CancellationToken cancellationToken = default)
    {
        console ??= new SystemAccountConsole();
        if (!console.IsInteractive)
        {
            console.WriteLine("Fehler: 'account set' benötigt ein interaktives Terminal.");
            console.WriteLine("Error: 'account set' requires an interactive terminal.");
            return 2;
        }

        try
        {
            using var instanceLock = new InstanceLock(paths);
            if (!File.Exists(paths.DatabasePath))
            {
                console.WriteLine("Keine bestehende Ludus-Datenbank gefunden; neue Instanzen werden im Browser eingerichtet.");
                console.WriteLine("No existing Ludus database was found; set up new instances in the browser.");
                return 2;
            }
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);

            if (!TryReadCredentials(console, out var username, out var password)) return 2;
            var sessionVersion = await scope.ServiceProvider.GetRequiredService<AccountManager>()
                .SetCredentialsAsync(username, password, requiredSetupMode: null, cancellationToken);
            if (sessionVersion is null) throw new InvalidOperationException("The account could not be updated.");

            File.Delete(Path.Combine(paths.ConfigDirectory, "bootstrap-api-key"));
            File.Delete(Path.Combine(paths.ConfigDirectory, "account-setup-token"));
            File.Delete(Path.Combine(paths.ConfigDirectory, "reset-account"));
            console.WriteLine("Administratorkonto wurde gesetzt; vorhandene Sitzungen wurden ungültig gemacht.");
            console.WriteLine("Administrator account was set; existing sessions were invalidated.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            console.WriteLine("Kontoeinrichtung abgebrochen. / Account setup cancelled.");
            return 2;
        }
        catch (Exception exception)
        {
            console.WriteLine("Kontoeinrichtung fehlgeschlagen. Stoppe den Ludus-Dienst und versuche es erneut.");
            console.WriteLine("Account setup failed. Stop the Ludus service and try again.");
            console.WriteLine($"Fehlertyp / Error type: {exception.GetType().Name}");
            return 1;
        }
    }

    private static bool TryReadCredentials(IAccountConsole console, out string username, out string password)
    {
        console.Write("Benutzername / Username: ");
        username = console.ReadLine()?.Trim() ?? string.Empty;
        if (username.Length is 0 or > 128)
        {
            console.WriteLine("Der Benutzername muss 1 bis 128 Zeichen lang sein. / Username must contain 1 to 128 characters.");
            password = string.Empty;
            return false;
        }

        console.Write("Passwort / Password: ");
        password = console.ReadSecret();
        if (password.Length is < 12 or > 1024)
        {
            console.WriteLine("Das Passwort muss 12 bis 1024 Zeichen lang sein. / Password must contain 12 to 1024 characters.");
            password = string.Empty;
            return false;
        }

        console.Write("Passwort bestätigen / Confirm password: ");
        var confirmation = console.ReadSecret();
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            console.WriteLine("Die Passwörter stimmen nicht überein. / The passwords do not match.");
            password = string.Empty;
            return false;
        }

        return true;
    }
}

public interface IAccountConsole
{
    bool IsInteractive { get; }
    string? ReadLine();
    string ReadSecret();
    void Write(string value);
    void WriteLine(string value);
}

public sealed class SystemAccountConsole : IAccountConsole
{
    public bool IsInteractive => !Console.IsInputRedirected;
    public string? ReadLine() => Console.ReadLine();
    public void Write(string value) => Console.Write(value);
    public void WriteLine(string value) => Console.WriteLine(value);

    public string ReadSecret()
    {
        var characters = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string(characters.ToArray());
            }
            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                throw new OperationCanceledException();
            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count > 0) characters.RemoveAt(characters.Count - 1);
                continue;
            }
            if (!char.IsControl(key.KeyChar) && characters.Count < 1025) characters.Add(key.KeyChar);
        }
    }
}
