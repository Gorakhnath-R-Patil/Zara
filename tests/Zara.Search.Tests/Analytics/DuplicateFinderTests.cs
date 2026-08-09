using Microsoft.Data.Sqlite;
using Zara.Search.Analytics;
using Zara.Storage;

namespace Zara.Search.Tests.Analytics;

public class DuplicateFinderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DuplicateFinder _sut = new();

    public DuplicateFinderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-dupfinder-test-{Guid.NewGuid():N}.db");
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

    private void InsertFile(long id, string name, long size, long? quickHash, bool isDir = false, bool deleted = false)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO files (id, volume_id, frn, name, name_folded, path_hash, depth, is_dir,
                                size_bytes, quick_hash, indexed_utc, deleted_utc)
            VALUES ($id, 1, $id, $name, $name, $id, 1, $isDir, $size, $quickHash, 0, $deletedUtc);
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$isDir", isDir ? 1 : 0);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$quickHash", (object?)quickHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$deletedUtc", deleted ? 1 : (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection() => _connectionFactory.CreateConnection();

    [Fact]
    public async Task FindCandidatesAsync_TwoFilesWithSameSizeAndQuickHash_AreGrouped()
    {
        InsertFile(1, "a.txt", 100, quickHash: 42);
        InsertFile(2, "b.txt", 100, quickHash: 42);

        using var connection = OpenConnection();
        var groups = await _sut.FindCandidatesAsync(connection);

        var group = Assert.Single(groups);
        Assert.Equal(100, group.SizeBytes);
        Assert.Equal(2, group.Files.Count);
        Assert.Contains(group.Files, f => f.Name == "a.txt");
        Assert.Contains(group.Files, f => f.Name == "b.txt");
    }

    [Fact]
    public async Task FindCandidatesAsync_SingleFile_IsNotAGroup()
    {
        InsertFile(1, "unique.txt", 100, quickHash: 42);

        using var connection = OpenConnection();
        var groups = await _sut.FindCandidatesAsync(connection);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindCandidatesAsync_SameQuickHashDifferentSize_AreNotGrouped()
    {
        // A quick_hash collision with different sizes can't be a real
        // duplicate — size mismatch alone rules it out immediately.
        InsertFile(1, "a.txt", 100, quickHash: 42);
        InsertFile(2, "b.txt", 200, quickHash: 42);

        using var connection = OpenConnection();
        var groups = await _sut.FindCandidatesAsync(connection);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindCandidatesAsync_SameSizeDifferentQuickHash_AreNotGrouped()
    {
        InsertFile(1, "a.txt", 100, quickHash: 1);
        InsertFile(2, "b.txt", 100, quickHash: 2);

        using var connection = OpenConnection();
        var groups = await _sut.FindCandidatesAsync(connection);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindCandidatesAsync_FilesWithoutAQuickHash_AreIgnored()
    {
        InsertFile(1, "a.txt", 100, quickHash: null);
        InsertFile(2, "b.txt", 100, quickHash: null);

        using var connection = OpenConnection();
        var groups = await _sut.FindCandidatesAsync(connection);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindCandidatesAsync_Directories_AreNeverConsidered()
    {
        InsertFile(1, "dir-a", 0, quickHash: 42, isDir: true);
        InsertFile(2, "dir-b", 0, quickHash: 42, isDir: true);

        using var connection = OpenConnection();
        var groups = await _sut.FindCandidatesAsync(connection);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindCandidatesAsync_DeletedFiles_AreExcluded()
    {
        InsertFile(1, "a.txt", 100, quickHash: 42);
        InsertFile(2, "b.txt", 100, quickHash: 42, deleted: true);

        using var connection = OpenConnection();
        var groups = await _sut.FindCandidatesAsync(connection);

        Assert.Empty(groups); // only one live candidate remains — not a group
    }

    [Fact]
    public async Task FindCandidatesAsync_ThreeFilesSharingAHash_AreOneGroupOfThree()
    {
        InsertFile(1, "a.txt", 100, quickHash: 42);
        InsertFile(2, "b.txt", 100, quickHash: 42);
        InsertFile(3, "c.txt", 100, quickHash: 42);

        using var connection = OpenConnection();
        var groups = await _sut.FindCandidatesAsync(connection);

        var group = Assert.Single(groups);
        Assert.Equal(3, group.Files.Count);
    }

    [Fact]
    public async Task FindCandidatesAsync_MultipleIndependentGroups_AreAllFound()
    {
        InsertFile(1, "a1.txt", 100, quickHash: 1);
        InsertFile(2, "a2.txt", 100, quickHash: 1);
        InsertFile(3, "b1.txt", 200, quickHash: 2);
        InsertFile(4, "b2.txt", 200, quickHash: 2);
        InsertFile(5, "unique.txt", 300, quickHash: 3);

        using var connection = OpenConnection();
        var groups = await _sut.FindCandidatesAsync(connection);

        Assert.Equal(2, groups.Count);
    }

    // ── Confirm ──────────────────────────────────────────────────────────────

    [Fact]
    public void Confirm_AllFilesHaveTheSameContentHash_StaysOneGroup()
    {
        var candidate = new DuplicateGroup(100, [new DuplicateFile(1, "a.txt"), new DuplicateFile(2, "b.txt")]);

        var confirmed = _sut.Confirm(candidate, _ => "same-hash");

        var group = Assert.Single(confirmed);
        Assert.Equal(2, group.Files.Count);
    }

    [Fact]
    public void Confirm_QuickHashCollisionWithDifferentContent_SplitsIntoSeparateGroups()
    {
        // Exactly the scenario QuickHasherTests demonstrates deliberately:
        // three files share (size, quick_hash) but only two are genuinely
        // identical — the third differs in a part the quick hash doesn't sample.
        var candidate = new DuplicateGroup(100,
            [new DuplicateFile(1, "a.txt"), new DuplicateFile(2, "b.txt"), new DuplicateFile(3, "different.txt")]);

        var confirmed = _sut.Confirm(candidate, id => id == 3 ? "hash-B" : "hash-A");

        var group = Assert.Single(confirmed);
        Assert.Equal(2, group.Files.Count);
        Assert.DoesNotContain(group.Files, f => f.FileId == 3);
    }

    [Fact]
    public void Confirm_AllFilesHaveDifferentContentHashes_ProducesNoGroups()
    {
        var candidate = new DuplicateGroup(100, [new DuplicateFile(1, "a.txt"), new DuplicateFile(2, "b.txt")]);

        var confirmed = _sut.Confirm(candidate, id => $"unique-hash-{id}");

        Assert.Empty(confirmed);
    }
}
