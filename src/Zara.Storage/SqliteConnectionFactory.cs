using Microsoft.Data.Sqlite;

namespace Zara.Storage;

/// <inheritdoc cref="ISqliteConnectionFactory"/>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
        }.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        ApplyPragmas(connection);
        return connection;
    }

    // ARCHITECTURE.md §6.7's non-negotiable pragma set. WAL + NORMAL sync is
    // what lets the single writer thread (WriteQueue) and concurrent readers
    // coexist without SQLITE_BUSY; the cache/mmap sizes are tuned for the
    // 16GB-RAM target machine (§5), not left at SQLite's tiny defaults.
    private static void ApplyPragmas(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA cache_size = -65536;
            PRAGMA mmap_size = 268435456;
            PRAGMA temp_store = MEMORY;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA wal_autocheckpoint = 4000;
            """;
        cmd.ExecuteNonQuery();
    }
}
