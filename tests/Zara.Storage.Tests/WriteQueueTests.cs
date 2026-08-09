using Microsoft.Data.Sqlite;
using Zara.Storage;

namespace Zara.Storage.Tests;

public class WriteQueueTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private SqliteConnectionFactory _factory = null!;
    private WriteQueue _sut = null!;

    public WriteQueueTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-writequeue-test-{Guid.NewGuid():N}.db");
    }

    public Task InitializeAsync()
    {
        _factory = new SqliteConnectionFactory(_dbPath);
        using (var connection = _factory.CreateConnection())
        {
            new MigrationRunner().MigrateToLatest(connection);
        }
        _sut = new WriteQueue(_factory);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _sut.DisposeAsync();
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
    public async Task RunAsync_ExecutesTheWriteAndReturnsAfterItCompletes()
    {
        await _sut.RunAsync(connection => InsertVolume(connection, 1));

        using var reader = _factory.CreateConnection();
        Assert.Equal(1L, Scalar(reader, "SELECT COUNT(*) FROM volumes;"));
    }

    [Fact]
    public async Task RunAsyncOfT_ReturnsTheProducedValue()
    {
        long id = await _sut.RunAsync(connection =>
        {
            InsertVolume(connection, 7);
            return 7L;
        });

        Assert.Equal(7L, id);
    }

    [Fact]
    public async Task RunAsync_ConcurrentWrites_AllLandWithoutSqliteBusy()
    {
        // The point of the queue: many concurrent callers, one serialized
        // writer, zero SQLITE_BUSY errors from writer/writer contention.
        const int count = 200;
        var tasks = Enumerable.Range(1, count).Select(i => _sut.RunAsync(connection => InsertVolume(connection, i)));

        await Task.WhenAll(tasks);

        using var reader = _factory.CreateConnection();
        Assert.Equal((long)count, Scalar(reader, "SELECT COUNT(*) FROM volumes;"));
    }

    [Fact]
    public async Task RunAsync_ExceptionInWrite_PropagatesToCaller_QueueStaysUsable()
    {
        await Assert.ThrowsAsync<SqliteException>(() => _sut.RunAsync(connection =>
        {
            // volume_id 999 doesn't exist -> foreign key violation.
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO files (id, volume_id, frn, name, name_folded, path_hash,
                                    depth, is_dir, indexed_utc)
                VALUES (1, 999, 1, 'x', 'x', 1, 0, 0, 0);
                """;
            cmd.ExecuteNonQuery();
        }));

        // The queue's background worker must have survived the exception —
        // prove it by successfully running another write afterward.
        await _sut.RunAsync(connection => InsertVolume(connection, 1));

        using var reader = _factory.CreateConnection();
        Assert.Equal(1L, Scalar(reader, "SELECT COUNT(*) FROM volumes;"));
    }

    [Fact]
    public async Task DisposeAsync_DrainsPendingWritesBeforeStopping()
    {
        var localQueue = new WriteQueue(_factory);
        var tasks = Enumerable.Range(100, 20).Select(i => localQueue.RunAsync(connection => InsertVolume(connection, i))).ToList();

        await localQueue.DisposeAsync();
        await Task.WhenAll(tasks);

        using var reader = _factory.CreateConnection();
        Assert.Equal(20L, Scalar(reader, "SELECT COUNT(*) FROM volumes;"));
    }

    [Fact]
    public async Task RunAsync_AfterDispose_Throws()
    {
        var localQueue = new WriteQueue(_factory);
        await localQueue.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => localQueue.RunAsync(_ => { }));
    }

    private static void InsertVolume(SqliteConnection connection, long id)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO volumes (id, guid, serial, filesystem) VALUES ($id, $guid, $id, 'NTFS');";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$guid", $"vol-{id}-{Guid.NewGuid():N}");
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
