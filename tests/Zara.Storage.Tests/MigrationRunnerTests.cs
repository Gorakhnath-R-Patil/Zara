using Microsoft.Data.Sqlite;
using Zara.Storage;

namespace Zara.Storage.Tests;

public class MigrationRunnerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public MigrationRunnerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-migrate-test-{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_dbPath);
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
    public void MigrateToLatest_CreatesTheExpectedTables()
    {
        using var connection = _factory.CreateConnection();
        var sut = new MigrationRunner();

        sut.MigrateToLatest(connection);

        Assert.True(TableExists(connection, "volumes"));
        Assert.True(TableExists(connection, "files"));
        Assert.True(TableExists(connection, "folder_stats"));
    }

    [Fact]
    public void MigrateToLatest_SetsUserVersionToTheHighestMigration()
    {
        using var connection = _factory.CreateConnection();
        var sut = new MigrationRunner();

        sut.MigrateToLatest(connection);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        long version = (long)cmd.ExecuteScalar()!;

        Assert.Equal(1, version);
    }

    [Fact]
    public void MigrateToLatest_IsIdempotent_SecondCallDoesNothing()
    {
        using var connection = _factory.CreateConnection();
        var sut = new MigrationRunner();

        sut.MigrateToLatest(connection);
        var exception = Record.Exception(() => sut.MigrateToLatest(connection));

        Assert.Null(exception);
    }

    [Fact]
    public void MigrateToLatest_ForeignKeyAndCheckConstraintsAreUsable()
    {
        using var connection = _factory.CreateConnection();
        new MigrationRunner().MigrateToLatest(connection);

        using var insertVolume = connection.CreateCommand();
        insertVolume.CommandText = """
            INSERT INTO volumes (id, guid, serial, filesystem)
            VALUES (1, 'vol-guid', 12345, 'NTFS');
            """;
        insertVolume.ExecuteNonQuery();

        using var insertFile = connection.CreateCommand();
        insertFile.CommandText = """
            INSERT INTO files (id, volume_id, frn, name, name_folded, path_hash,
                                depth, is_dir, indexed_utc)
            VALUES (1, 1, 100, 'test.txt', 'test.txt', 999, 0, 0, 0);
            """;
        var exception = Record.Exception(() => insertFile.ExecuteNonQuery());

        Assert.Null(exception);
    }

    [Fact]
    public void MigrateToLatest_ForeignKeyViolation_IsRejected()
    {
        using var connection = _factory.CreateConnection();
        new MigrationRunner().MigrateToLatest(connection);

        using var insertFile = connection.CreateCommand();
        // volume_id 999 doesn't exist — foreign_keys=ON must reject this.
        insertFile.CommandText = """
            INSERT INTO files (id, volume_id, frn, name, name_folded, path_hash,
                                depth, is_dir, indexed_utc)
            VALUES (1, 999, 100, 'test.txt', 'test.txt', 1, 0, 0, 0);
            """;

        Assert.Throws<SqliteException>(() => insertFile.ExecuteNonQuery());
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", tableName);
        return cmd.ExecuteScalar() is not null;
    }
}
