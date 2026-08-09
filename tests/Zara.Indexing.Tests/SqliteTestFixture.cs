using Microsoft.Data.Sqlite;
using Zara.Storage;

namespace Zara.Indexing.Tests;

/// <summary>Common setup: a fresh migrated database file + a WriteQueue over it,
/// torn down after each test. Shared by the Writing/Checkpointing/Scan test
/// classes rather than duplicated three times.</summary>
public abstract class SqliteTestFixture : IAsyncLifetime
{
    private string _dbPath = null!;
    protected SqliteConnectionFactory ConnectionFactory { get; private set; } = null!;
    protected WriteQueue WriteQueue { get; private set; } = null!;

    public Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-indexing-test-{Guid.NewGuid():N}.db");
        ConnectionFactory = new SqliteConnectionFactory(_dbPath);

        using (var connection = ConnectionFactory.CreateConnection())
        {
            new MigrationRunner().MigrateToLatest(connection);
        }

        WriteQueue = new WriteQueue(ConnectionFactory);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await WriteQueue.DisposeAsync();
        SqliteConnection.ClearAllPools();

        foreach (string file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    protected long InsertVolume(string guid = "test-volume")
    {
        using var connection = ConnectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO volumes (guid, serial, filesystem) VALUES ($guid, 1, 'NTFS'); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$guid", guid);
        return (long)cmd.ExecuteScalar()!;
    }

    protected long CountFiles()
    {
        using var connection = ConnectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM files;";
        return (long)cmd.ExecuteScalar()!;
    }
}
