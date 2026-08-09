using Microsoft.Data.Sqlite;
using Zara.Search.Query;
using Zara.Storage;

namespace Zara.Search.Tests.Query;

/// <summary>
/// Executes QueryPlanner's generated SQL against a REAL SQLite database
/// (migrated with the real 001_initial.sql schema) rather than only
/// inspecting the WHERE-clause string. A plan that "looks right" as text can
/// still be wrong SQL — parameter name mismatches, wrong bitwise operators,
/// wrong LIKE escaping — and only actually running it catches that class of
/// bug (the same lesson T04/T13's real-execution tests already paid for).
/// </summary>
public class QueryPlannerIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly QueryPlanner _sut = new();

    public QueryPlannerIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-queryplanner-test-{Guid.NewGuid():N}.db");
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

    private void InsertFile(
        long id, string name, string? ext, long sizeBytes,
        long modifiedUtc = 0, long createdUtc = 0, long accessedUtc = 0,
        FileAttributes attributes = FileAttributes.Archive, int depth = 1, bool deleted = false)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO files (id, volume_id, frn, name, name_folded, ext, path_hash, depth, is_dir,
                                size_bytes, created_utc, modified_utc, accessed_utc, attributes, indexed_utc, deleted_utc)
            VALUES ($id, 1, $id, $name, $nameFolded, $ext, $id, $depth, 0,
                    $size, $created, $modified, $accessed, $attributes, 0, $deletedUtc);
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$nameFolded", name.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$ext", (object?)ext ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$depth", depth);
        cmd.Parameters.AddWithValue("$size", sizeBytes);
        cmd.Parameters.AddWithValue("$created", createdUtc);
        cmd.Parameters.AddWithValue("$modified", modifiedUtc);
        cmd.Parameters.AddWithValue("$accessed", accessedUtc);
        cmd.Parameters.AddWithValue("$attributes", (int)attributes);
        cmd.Parameters.AddWithValue("$deletedUtc", deleted ? 1 : (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Runs the plan for real and returns the matching names, in the
    /// plan's own order — this is the thing that actually proves the SQL works.</summary>
    private List<string> Execute(StructuredQuery query)
    {
        var plan = _sut.Plan(query);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT name FROM files WHERE {plan.WhereClause} {plan.OrderByClause} LIMIT {plan.Limit};";
        foreach (var (key, value) in plan.Parameters)
        {
            cmd.Parameters.AddWithValue(key, value);
        }

        var results = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    [Fact]
    public void Plan_Extension_FiltersCorrectly()
    {
        InsertFile(1, "a.pdf", "pdf", 100);
        InsertFile(2, "b.txt", "txt", 100);

        var results = Execute(DslParser.Parse("ext:pdf"));

        Assert.Equal(["a.pdf"], results);
    }

    [Fact]
    public void Plan_ExcludedExtension_FiltersCorrectly()
    {
        InsertFile(1, "a.pdf", "pdf", 100);
        InsertFile(2, "b.tmp", "tmp", 100);
        InsertFile(3, "c.txt", "txt", 100);

        var results = Execute(DslParser.Parse("NOT ext:tmp"));

        Assert.Equal(["a.pdf", "c.txt"], results.OrderBy(n => n));
    }

    [Fact]
    public void Plan_SizeRange_FiltersCorrectly()
    {
        InsertFile(1, "small.bin", "bin", 100);
        InsertFile(2, "medium.bin", "bin", 5000);
        InsertFile(3, "large.bin", "bin", 50000);

        var results = Execute(DslParser.Parse("size:1000..10000"));

        Assert.Equal(["medium.bin"], results);
    }

    [Fact]
    public void Plan_ModifiedDateRange_FiltersCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        InsertFile(1, "recent.txt", "txt", 1, modifiedUtc: now.ToUnixTimeMilliseconds());
        InsertFile(2, "old.txt", "txt", 1, modifiedUtc: now.AddDays(-100).ToUnixTimeMilliseconds());

        var results = Execute(DslParser.Parse("modified:<7d"));

        Assert.Equal(["recent.txt"], results);
    }

    [Fact]
    public void Plan_TypeClass_ExpandsToExtensionListCorrectly()
    {
        InsertFile(1, "photo.jpg", "jpg", 1);
        InsertFile(2, "clip.mp4", "mp4", 1);
        InsertFile(3, "doc.pdf", "pdf", 1);

        var results = Execute(DslParser.Parse("type:image"));

        Assert.Equal(["photo.jpg"], results);
    }

    [Fact]
    public void Plan_Attribute_BitwiseFilterWorksCorrectly()
    {
        InsertFile(1, "visible.txt", "txt", 1, attributes: FileAttributes.Archive);
        InsertFile(2, "hidden.txt", "txt", 1, attributes: FileAttributes.Hidden | FileAttributes.Archive);

        var results = Execute(DslParser.Parse("attr:hidden"));

        Assert.Equal(["hidden.txt"], results);
    }

    [Fact]
    public void Plan_Depth_FiltersCorrectly()
    {
        InsertFile(1, "shallow.txt", "txt", 1, depth: 1);
        InsertFile(2, "deep.txt", "txt", 1, depth: 3);

        var results = Execute(DslParser.Parse("depth:1"));

        Assert.Equal(["shallow.txt"], results);
    }

    [Fact]
    public void Plan_NameTerm_LikeSearch_IsCaseInsensitiveSubstring()
    {
        InsertFile(1, "MyResume.pdf", "pdf", 1);
        InsertFile(2, "photo.jpg", "jpg", 1);

        var results = Execute(DslParser.Parse("resume"));

        Assert.Equal(["MyResume.pdf"], results);
    }

    [Fact]
    public void Plan_NameTermContainingPercentAndUnderscore_IsEscapedNotWildcarded()
    {
        // A literal '%' or '_' in the search term must not act as a SQL LIKE
        // wildcard — proves ToLikePattern's escaping actually works, not just
        // that it compiles.
        InsertFile(1, "100%_done.txt", "txt", 1);
        InsertFile(2, "1000xdone.txt", "txt", 1); // would ALSO match if % / _ were literal wildcards

        var results = Execute(DslParser.Parse("100%_done"));

        Assert.Equal(["100%_done.txt"], results);
    }

    [Fact]
    public void Plan_DeletedFiles_AreAlwaysExcluded()
    {
        InsertFile(1, "alive.txt", "txt", 1);
        InsertFile(2, "gone.txt", "txt", 1, deleted: true);

        var results = Execute(DslParser.Parse("ext:txt"));

        Assert.Equal(["alive.txt"], results);
    }

    [Fact]
    public void Plan_CombinedPredicates_AllApplyTogether()
    {
        InsertFile(1, "match.pdf", "pdf", 5000, modifiedUtc: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        InsertFile(2, "wrong-ext.txt", "txt", 5000, modifiedUtc: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        InsertFile(3, "too-small.pdf", "pdf", 10, modifiedUtc: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        InsertFile(4, "too-old.pdf", "pdf", 5000, modifiedUtc: DateTimeOffset.UtcNow.AddDays(-100).ToUnixTimeMilliseconds());

        var results = Execute(DslParser.Parse("ext:pdf size:>1000 modified:<7d"));

        Assert.Equal(["match.pdf"], results);
    }

    [Fact]
    public void Plan_SortBySize_Descending()
    {
        InsertFile(1, "small.bin", "bin", 10);
        InsertFile(2, "large.bin", "bin", 1000);
        InsertFile(3, "medium.bin", "bin", 100);

        var results = Execute(DslParser.Parse("ext:bin sort:size"));

        Assert.Equal(["large.bin", "medium.bin", "small.bin"], results);
    }

    [Fact]
    public void Plan_SortBySize_Ascending()
    {
        InsertFile(1, "small.bin", "bin", 10);
        InsertFile(2, "large.bin", "bin", 1000);

        var results = Execute(DslParser.Parse("ext:bin sort:size asc"));

        Assert.Equal(["small.bin", "large.bin"], results);
    }

    [Fact]
    public void Plan_Limit_IsRespected()
    {
        for (int i = 0; i < 10; i++)
        {
            InsertFile(i, $"file{i}.bin", "bin", i);
        }

        var results = Execute(DslParser.Parse("ext:bin limit:3"));

        Assert.Equal(3, results.Count);
    }

    // ── Unsupported predicates are surfaced, not silently dropped ──────────

    [Theory]
    [InlineData("path:Downloads", "path")]
    [InlineData(@"in:C:\Projects", "in")]
    [InlineData("dup:true", "dup")]
    [InlineData("empty:true", "empty")]
    [InlineData("content:\"kafka\"", "content")]
    public void Plan_UnsupportedPredicate_IsListedNotSilentlyIgnored(string queryText, string expectedTag)
    {
        var plan = _sut.Plan(DslParser.Parse(queryText));

        Assert.Contains(expectedTag, plan.UnsupportedPredicates);
    }

    [Fact]
    public void Plan_UnrecognizedAttribute_IsListedAsUnsupported()
    {
        var plan = _sut.Plan(DslParser.Parse("attr:something-made-up"));

        Assert.Contains("attr:something-made-up", plan.UnsupportedPredicates);
    }

    [Fact]
    public void Plan_UnrecognizedTypeClass_IsListedAsUnsupported()
    {
        var plan = _sut.Plan(DslParser.Parse("type:made-up-type"));

        Assert.Contains("type:made-up-type", plan.UnsupportedPredicates);
    }

    [Fact]
    public void Plan_FullySupportedQuery_HasNoUnsupportedPredicates()
    {
        var plan = _sut.Plan(DslParser.Parse("ext:pdf size:>1mb modified:<7d attr:hidden"));

        Assert.Empty(plan.UnsupportedPredicates);
    }
}
