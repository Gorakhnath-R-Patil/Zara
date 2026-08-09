using Microsoft.Data.Sqlite;
using Zara.Storage;

namespace Zara.Storage.Tests;

public class SqliteConnectionFactoryTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteConnectionFactoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    [Fact]
    public void CreateConnection_OpensAndAppliesWalJournalMode()
    {
        var factory = new SqliteConnectionFactory(_dbPath);

        using var connection = factory.CreateConnection();

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
        Assert.Equal("wal", QueryScalar(connection, "PRAGMA journal_mode;"));
    }

    [Fact]
    public void CreateConnection_AppliesForeignKeysAndSynchronousPragmas()
    {
        var factory = new SqliteConnectionFactory(_dbPath);

        using var connection = factory.CreateConnection();

        Assert.Equal(1L, QueryScalar(connection, "PRAGMA foreign_keys;"));
        Assert.Equal(1L, QueryScalar(connection, "PRAGMA synchronous;")); // NORMAL = 1
    }

    [Fact]
    public void CreateConnection_MultipleConnections_ShareTheSameDatabaseFile()
    {
        var factory = new SqliteConnectionFactory(_dbPath);

        using var writer = factory.CreateConnection();
        using (var cmd = writer.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (42);";
            cmd.ExecuteNonQuery();
        }

        using var reader = factory.CreateConnection();
        Assert.Equal(42L, QueryScalar(reader, "SELECT id FROM t;"));
    }

    private static object? QueryScalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
