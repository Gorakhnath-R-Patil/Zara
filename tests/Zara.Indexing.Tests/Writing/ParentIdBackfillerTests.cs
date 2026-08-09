using Zara.Core.Files;
using Zara.Filesystem.Enumeration;
using Zara.Filesystem.Paths;
using Zara.Indexing.Checkpointing;
using Zara.Indexing.Scan;
using Zara.Indexing.Writing;
using Zara.Volumes.Fallback;
using Zara.Volumes.Skip;

namespace Zara.Indexing.Tests.Writing;

/// <summary>
/// Proves the parent_id backfill end to end: a real ScanOrchestrator run
/// against a real nested directory tree, then ParentIdBackfiller, then walks
/// the resulting parent_id chain in SQL and checks it reconstructs the
/// correct path — the thing this mechanism exists to make possible.
/// </summary>
public class ParentIdBackfillerTests : SqliteTestFixture, IDisposable
{
    private readonly string _tempRoot;
    private readonly PathCanonicalizer _canonicalizer = new();
    private readonly NtDirectoryEnumerator _directoryEnumerator = new();

    public ParentIdBackfillerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-parentid-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch (IOException) { }
    }

    private sealed class NoOpSkipList : ISkipList
    {
        public bool ShouldSkip(string canonicalPath, bool isDirectory) => false;
    }

    [Fact]
    public async Task RunAsync_PopulatesParentIdForNestedTree_RegardlessOfWriteOrder()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "a", "b"));
        File.WriteAllText(Path.Combine(_tempRoot, "a", "b", "leaf.txt"), "1");

        long volumeId = InsertVolume();
        var orchestrator = new ScanOrchestrator(
            _directoryEnumerator,
            new WalkScanner(_directoryEnumerator),
            new FileIndexWriter(WriteQueue),
            new ScanCheckpointStore(ConnectionFactory, WriteQueue),
            new NoOpSkipList());

        var root = _canonicalizer.CanonicalizeExisting(_tempRoot);
        await orchestrator.ScanAsync(volumeId, root);

        var backfiller = new ParentIdBackfiller(WriteQueue);
        int touched = await backfiller.RunAsync(volumeId);

        Assert.True(touched > 0);

        // Walk the parent_id chain starting from "leaf.txt" and reconstruct
        // the name sequence — this is the actual capability parent_id exists
        // to provide (§22: "reconstructed by walking parent_id").
        using var connection = ConnectionFactory.CreateConnection();
        var names = new List<string>();
        long? currentId = GetIdByName(connection, "leaf.txt");

        while (currentId is { } id)
        {
            var (name, parentId) = GetNameAndParent(connection, id);
            names.Add(name);
            currentId = parentId;
        }

        names.Reverse();
        Assert.Equal(["a", "b", "leaf.txt"], names);
    }

    [Fact]
    public async Task RunAsync_TopLevelEntries_HaveNullParentId_BecauseScanRootItselfIsNeverIndexed()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "root-file.txt"), "1");
        long volumeId = InsertVolume();

        var orchestrator = new ScanOrchestrator(
            _directoryEnumerator,
            new WalkScanner(_directoryEnumerator),
            new FileIndexWriter(WriteQueue),
            new ScanCheckpointStore(ConnectionFactory, WriteQueue),
            new NoOpSkipList());

        var root = _canonicalizer.CanonicalizeExisting(_tempRoot);
        await orchestrator.ScanAsync(volumeId, root);
        await new ParentIdBackfiller(WriteQueue).RunAsync(volumeId);

        using var connection = ConnectionFactory.CreateConnection();
        var (_, parentId) = GetNameAndParent(connection, GetIdByName(connection, "root-file.txt")!.Value);

        Assert.Null(parentId);
    }

    [Fact]
    public async Task RunAsync_IsIdempotent()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "a"));
        File.WriteAllText(Path.Combine(_tempRoot, "a", "f.txt"), "1");
        long volumeId = InsertVolume();

        var orchestrator = new ScanOrchestrator(
            _directoryEnumerator,
            new WalkScanner(_directoryEnumerator),
            new FileIndexWriter(WriteQueue),
            new ScanCheckpointStore(ConnectionFactory, WriteQueue),
            new NoOpSkipList());

        await orchestrator.ScanAsync(volumeId, _canonicalizer.CanonicalizeExisting(_tempRoot));

        var backfiller = new ParentIdBackfiller(WriteQueue);
        await backfiller.RunAsync(volumeId);
        var exception = await Record.ExceptionAsync(() => backfiller.RunAsync(volumeId));

        Assert.Null(exception);
    }

    private static long? GetIdByName(Microsoft.Data.Sqlite.SqliteConnection connection, string name)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM files WHERE name = $name;";
        cmd.Parameters.AddWithValue("$name", name);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private static (string Name, long? ParentId) GetNameAndParent(Microsoft.Data.Sqlite.SqliteConnection connection, long id)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, parent_id FROM files WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        reader.Read();
        string name = reader.GetString(0);
        long? parentId = reader.IsDBNull(1) ? null : reader.GetInt64(1);
        return (name, parentId);
    }
}
