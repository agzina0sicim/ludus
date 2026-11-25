using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Persistence;

public static class DatabaseOptions
{
    public static void Configure(DbContextOptionsBuilder options, string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 30,
            Pooling = true
        }.ToString();

        options.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(30));
    }
}
