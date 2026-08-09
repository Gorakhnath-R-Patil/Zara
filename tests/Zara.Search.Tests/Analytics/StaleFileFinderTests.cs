using Microsoft.Data.Sqlite;
using Zara.Search.Analytics;
using Zara.Storage;

namespace Zara.Search.Tests.Analytics;

public class StaleFileFinderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly StaleFileFinder _sut = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public StaleFileFinderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-stale-test-{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        using var connection = _connectionFactory.CreateConnection();
        new MigrationRunner().MigrateToLatest(connection);

        using var insertVolume = connection.CreateCommand();
        insertVolume.CommandText = "INSERT INTO volumes (id, guid, serial, filesystem) VALUES (1, 'v', 1, 'NTFS');";
        insertVolume.ExecuteNonQuery();
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

    private void InsertFile(long id, string name, DateTimeOffset? modified, long size = 100, bool isDir = false, bool deleted = false)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO files (id, volume_id, frn, name, name_folded, path_hash, depth, is_dir,
                                size_bytes, modified_utc, indexed_utc, deleted_utc)
            VALUES ($id, 1, $id, $name, $name, $id, 1, $isDir, $size, $modified, 0, $deletedUtc);
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$isDir", isDir ? 1 : 0);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$modified", (object?)modified?.ToUnixTimeMilliseconds() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$deletedUtc", deleted ? 1 : (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection() => _connectionFactory.CreateConnection();

    [Fact]
    public async Task FindAsync_OldFile_IsReturned()
    {
        InsertFile(1, "old.txt", Now.AddDays(-200));

        using var connection = OpenConnection();
        var results = await _sut.FindAsync(connection, volumeId: 1, olderThan: TimeSpan.FromDays(180));

        Assert.Single(results);
        Assert.Equal("old.txt", results[0].Name);
    }

    [Fact]
    public async Task FindAsync_RecentFile_IsExcluded()
    {
        InsertFile(1, "recent.txt", Now.AddDays(-5));

        using var connection = OpenConnection();
        var results = await _sut.FindAsync(connection, volumeId: 1, olderThan: TimeSpan.FromDays(180));

        Assert.Empty(results);
    }

    [Fact]
    public async Task FindAsync_FileWithNoModifiedDate_IsExcluded()
    {
        InsertFile(1, "unknown.txt", modified: null);

        using var connection = OpenConnection();
        var results = await _sut.FindAsync(connection, volumeId: 1, olderThan: TimeSpan.FromDays(1));

        Assert.Empty(results);
    }

    [Fact]
    public async Task FindAsync_Directories_AreExcluded()
    {
        InsertFile(1, "old-dir", Now.AddDays(-200), isDir: true);

        using var connection = OpenConnection();
        var results = await _sut.FindAsync(connection, volumeId: 1, olderThan: TimeSpan.FromDays(180));

        Assert.Empty(results);
    }

    [Fact]
    public async Task FindAsync_DeletedFiles_AreExcluded()
    {
        InsertFile(1, "gone.txt", Now.AddDays(-200), deleted: true);

        using var connection = OpenConnection();
        var results = await _sut.FindAsync(connection, volumeId: 1, olderThan: TimeSpan.FromDays(180));

        Assert.Empty(results);
    }

    [Fact]
    public async Task FindAsync_OrdersLargestFirst()
    {
        InsertFile(1, "small-old.txt", Now.AddDays(-200), size: 10);
        InsertFile(2, "large-old.txt", Now.AddDays(-200), size: 1000);
        InsertFile(3, "medium-old.txt", Now.AddDays(-200), size: 100);

        using var connection = OpenConnection();
        var results = await _sut.FindAsync(connection, volumeId: 1, olderThan: TimeSpan.FromDays(180));

        Assert.Equal(["large-old.txt", "medium-old.txt", "small-old.txt"], results.Select(r => r.Name));
    }
}
