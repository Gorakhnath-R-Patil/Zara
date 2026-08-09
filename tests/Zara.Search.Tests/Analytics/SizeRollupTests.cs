using Microsoft.Data.Sqlite;
using Zara.Search.Analytics;
using Zara.Storage;

namespace Zara.Search.Tests.Analytics;

public class SizeRollupTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SizeRollup _sut = new();

    public SizeRollupTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-sizerollup-test-{Guid.NewGuid():N}.db");
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

    private void InsertRow(long id, string name, long? parentId, bool isDir, long size = 0, long? modified = null, int depth = 1)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO files (id, volume_id, frn, name, name_folded, path_hash, parent_id, depth, is_dir,
                                size_bytes, modified_utc, indexed_utc)
            VALUES ($id, 1, $id, $name, $name, $id, $parentId, $depth, $isDir, $size, $modified, 0);
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$parentId", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$depth", depth);
        cmd.Parameters.AddWithValue("$isDir", isDir ? 1 : 0);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$modified", (object?)modified ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private (int DirectFiles, long TotalFiles, long TotalBytes, long? MaxModified) GetStats(long fileId)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT direct_files, total_files, total_bytes, max_modified_utc FROM folder_stats WHERE file_id = $id;";
        cmd.Parameters.AddWithValue("$id", fileId);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read(), $"No folder_stats row for file_id={fileId}");
        return (
            reader.GetInt32(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3));
    }

    [Fact]
    public async Task RecomputeAsync_SingleDirectoryWithFiles_ComputesDirectTotals()
    {
        InsertRow(1, "a", parentId: null, isDir: true, depth: 1);
        InsertRow(2, "f1.txt", parentId: 1, isDir: false, size: 100, modified: 1000, depth: 2);
        InsertRow(3, "f2.txt", parentId: 1, isDir: false, size: 200, modified: 2000, depth: 2);

        using var connection = _connectionFactory.CreateConnection();
        await _sut.RecomputeAsync(connection, volumeId: 1);

        var stats = GetStats(1);
        Assert.Equal(2, stats.DirectFiles);
        Assert.Equal(2, stats.TotalFiles);
        Assert.Equal(300, stats.TotalBytes);
        Assert.Equal(2000, stats.MaxModified);
    }

    [Fact]
    public async Task RecomputeAsync_NestedDirectories_PropagateTotalsUpward()
    {
        InsertRow(1, "a", parentId: null, isDir: true, depth: 1);
        InsertRow(2, "b", parentId: 1, isDir: true, depth: 2);
        InsertRow(3, "file1.txt", parentId: 1, isDir: false, size: 100, modified: 1000, depth: 2);
        InsertRow(4, "file2.txt", parentId: 2, isDir: false, size: 200, modified: 2000, depth: 3);

        using var connection = _connectionFactory.CreateConnection();
        await _sut.RecomputeAsync(connection, volumeId: 1);

        var bStats = GetStats(2);
        Assert.Equal(1, bStats.DirectFiles);
        Assert.Equal(1, bStats.TotalFiles);
        Assert.Equal(200, bStats.TotalBytes);

        var aStats = GetStats(1);
        // "a" has 1 DIRECT file (file1.txt) — "b" is a subdirectory, not a
        // direct file — but 2 TOTAL files (file1.txt + b's file2.txt).
        Assert.Equal(1, aStats.DirectFiles);
        Assert.Equal(2, aStats.TotalFiles);
        Assert.Equal(300, aStats.TotalBytes);
        Assert.Equal(2000, aStats.MaxModified); // max(1000, 2000)
    }

    [Fact]
    public async Task RecomputeAsync_ThreeLevelsDeep_PropagatesAllTheWayToTheTop()
    {
        InsertRow(1, "a", parentId: null, isDir: true, depth: 1);
        InsertRow(2, "b", parentId: 1, isDir: true, depth: 2);
        InsertRow(3, "c", parentId: 2, isDir: true, depth: 3);
        InsertRow(4, "deep.txt", parentId: 3, isDir: false, size: 500, modified: 5000, depth: 4);

        using var connection = _connectionFactory.CreateConnection();
        await _sut.RecomputeAsync(connection, volumeId: 1);

        Assert.Equal((0, 1, 500L, 5000L), GetStats(1));
        Assert.Equal((0, 1, 500L, 5000L), GetStats(2));
        Assert.Equal((1, 1, 500L, 5000L), GetStats(3));
    }

    [Fact]
    public async Task RecomputeAsync_EmptyDirectory_HasZeroedStats()
    {
        InsertRow(1, "empty", parentId: null, isDir: true, depth: 1);

        using var connection = _connectionFactory.CreateConnection();
        await _sut.RecomputeAsync(connection, volumeId: 1);

        var stats = GetStats(1);
        Assert.Equal(0, stats.DirectFiles);
        Assert.Equal(0, stats.TotalFiles);
        Assert.Equal(0, stats.TotalBytes);
        Assert.Null(stats.MaxModified);
    }

    [Fact]
    public async Task RecomputeAsync_DeletedFiles_AreExcludedFromTotals()
    {
        using (var connection = _connectionFactory.CreateConnection())
        {
            InsertRow(1, "a", parentId: null, isDir: true, depth: 1);
            InsertRow(2, "live.txt", parentId: 1, isDir: false, size: 100, depth: 2);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO files (id, volume_id, frn, name, name_folded, path_hash, parent_id, depth, is_dir,
                                    size_bytes, indexed_utc, deleted_utc)
                VALUES (3, 1, 3, 'gone.txt', 'gone.txt', 3, 1, 2, 0, 999, 0, 1);
                """;
            cmd.ExecuteNonQuery();
        }

        using var readConnection = _connectionFactory.CreateConnection();
        await _sut.RecomputeAsync(readConnection, volumeId: 1);

        var stats = GetStats(1);
        Assert.Equal(1, stats.DirectFiles);
        Assert.Equal(100, stats.TotalBytes); // the deleted 999-byte file must not be counted
    }

    [Fact]
    public async Task RecomputeAsync_IsIdempotent_ReRunningProducesTheSameResult()
    {
        InsertRow(1, "a", parentId: null, isDir: true, depth: 1);
        InsertRow(2, "f.txt", parentId: 1, isDir: false, size: 100, modified: 1000, depth: 2);

        using var connection = _connectionFactory.CreateConnection();
        await _sut.RecomputeAsync(connection, volumeId: 1);
        var first = GetStats(1);
        await _sut.RecomputeAsync(connection, volumeId: 1);
        var second = GetStats(1);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task RecomputeAsync_ReturnsTheNumberOfDirectoriesUpdated()
    {
        InsertRow(1, "a", parentId: null, isDir: true, depth: 1);
        InsertRow(2, "b", parentId: 1, isDir: true, depth: 2);
        InsertRow(3, "f.txt", parentId: 2, isDir: false, size: 1, depth: 3);

        using var connection = _connectionFactory.CreateConnection();
        int updated = await _sut.RecomputeAsync(connection, volumeId: 1);

        Assert.Equal(2, updated); // two directories ("a", "b"); the file itself isn't a folder_stats row
    }
}
