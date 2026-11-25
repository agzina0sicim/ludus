using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ludus.Infrastructure.Persistence;

public sealed class DatabaseInitializer(AppDbContext database, DatabaseSeeder seeder, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = database.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            var journalMode = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The database filesystem does not support SQLite WAL mode.");
            }

            await database.Database.MigrateAsync(cancellationToken);
            await seeder.SeedAsync(cancellationToken);
            logger.LogInformation("Database migrations and initial configuration are ready; SQLite uses WAL.");
        }
        finally
        {
            await database.Database.CloseConnectionAsync();
        }
    }
}
