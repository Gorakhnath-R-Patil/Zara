using Microsoft.Data.Sqlite;
using Zara.Search.Analytics;
using Zara.Storage;

namespace Zara.Search.Tests.Analytics;

public class EmptyFolderFinderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly EmptyFolderFinder _sut = new();
    private readonly SizeRollup _sizeRollup = new();

    public EmptyFolderFinderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-emptyfolder-test-{Guid.NewGuid():N}.db");
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

    private void InsertRow(long id, string name, long? parentId, bool isDir, long size = 0, int depth = 1)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO files (id, volume_id, frn, name, name_folded, path_hash, parent_id, depth, is_dir,
                                size_bytes, indexed_utc)
            VALUES ($id, 1, $id, $name, $name, $id, $parentId, $depth, $isDir, $size, 0);
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$parentId", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$depth", depth);
        cmd.Parameters.AddWithValue("$isDir", isDir ? 1 : 0);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.ExecuteNonQuery();
    }

    private async Task<IReadOnlyList<EmptyFolder>> FindAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        await _sizeRollup.RecomputeAsync(connection, volumeId: 1); // EmptyFolderFinder depends on folder_stats
        return await _sut.FindAsync(connection, volumeId: 1);
    }

    [Fact]
    public async Task FindAsync_DirectoryWithNoFiles_IsFound()
    {
        InsertRow(1, "empty", parentId: null, isDir: true);

        var results = await FindAsync();

        Assert.Single(results);
        Assert.Equal("empty", results[0].Name);
    }

    [Fact]
    public async Task FindAsync_DirectoryWithAFile_IsNotFound()
    {
        InsertRow(1, "has-content", parentId: null, isDir: true);
        InsertRow(2, "f.txt", parentId: 1, isDir: false, size: 10, depth: 2);

        var results = await FindAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task FindAsync_DirectoryContainingOnlyEmptySubdirectories_IsAlsoFound()
    {
        // Recursively empty: "outer" has no files anywhere in its subtree,
        // even though it contains a (also empty) subdirectory.
        InsertRow(1, "outer", parentId: null, isDir: true);
        InsertRow(2, "inner", parentId: 1, isDir: true, depth: 2);

        var results = await FindAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, f => f.Name == "outer");
        Assert.Contains(results, f => f.Name == "inner");
    }

    [Fact]
    public async Task FindAsync_DirectoryWithNestedFile_IsNotFoundEvenThoughNotADirectChild()
    {
        InsertRow(1, "outer", parentId: null, isDir: true);
        InsertRow(2, "inner", parentId: 1, isDir: true, depth: 2);
        InsertRow(3, "deep.txt", parentId: 2, isDir: false, size: 1, depth: 3);

        var results = await FindAsync();

        // Neither "outer" nor "inner" is empty — "inner" has a direct file,
        // "outer" has one recursively.
        Assert.Empty(results);
    }
}
