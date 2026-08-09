using Zara.Core.Files;
using Zara.Indexing.Writing;
using Zara.Volumes.Fallback;

namespace Zara.Indexing.Tests.Writing;

public class FileIndexWriterTests : SqliteTestFixture
{
    private FileIndexWriter Sut => new(WriteQueue);

    [Fact]
    public async Task UpsertBatchAsync_WritesEntriesWithAFrn()
    {
        long volumeId = InsertVolume();
        var batch = new[] { MakeEntry("report.pdf", frn: 100, size: 2048) };

        var result = await Sut.UpsertBatchAsync(volumeId, batch);

        Assert.Equal(1, result.Written);
        Assert.Equal(0, result.SkippedNoFrn);
        Assert.Equal(1, CountFiles());
    }

    [Fact]
    public async Task UpsertBatchAsync_SkipsEntriesWithoutAFrn()
    {
        long volumeId = InsertVolume();
        var batch = new[] { MakeEntry("no-frn.txt", frn: null, size: 10) };

        var result = await Sut.UpsertBatchAsync(volumeId, batch);

        Assert.Equal(0, result.Written);
        Assert.Equal(1, result.SkippedNoFrn);
        Assert.Equal(0, CountFiles());
    }

    [Fact]
    public async Task UpsertBatchAsync_SameFrnTwice_UpdatesInPlaceRatherThanDuplicating()
    {
        long volumeId = InsertVolume();

        await Sut.UpsertBatchAsync(volumeId, [MakeEntry("v1.txt", frn: 500, size: 10)]);
        await Sut.UpsertBatchAsync(volumeId, [MakeEntry("renamed.txt", frn: 500, size: 20)]);

        Assert.Equal(1, CountFiles());

        using var connection = ConnectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, size_bytes FROM files;";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("renamed.txt", reader.GetString(0));
        Assert.Equal(20L, reader.GetInt64(1));
    }

    [Fact]
    public async Task UpsertBatchAsync_ExtractsLowercaseExtension()
    {
        long volumeId = InsertVolume();

        await Sut.UpsertBatchAsync(volumeId, [MakeEntry("Report.PDF", frn: 1, size: 1)]);

        using var connection = ConnectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ext FROM files;";
        Assert.Equal("pdf", (string)cmd.ExecuteScalar()!);
    }

    [Fact]
    public async Task UpsertBatchAsync_DirectoryHasNullExtension()
    {
        long volumeId = InsertVolume();

        await Sut.UpsertBatchAsync(volumeId, [MakeEntry("a-folder", frn: 1, size: 0, isDirectory: true)]);

        using var connection = ConnectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ext FROM files;";
        Assert.Equal(DBNull.Value, cmd.ExecuteScalar());
    }

    [Fact]
    public async Task UpsertBatchAsync_MixedFrnAndNoFrn_WritesOnlyTheFrnOnes()
    {
        long volumeId = InsertVolume();
        var batch = new[]
        {
            MakeEntry("a.txt", frn: 1, size: 1),
            MakeEntry("b.txt", frn: null, size: 1),
            MakeEntry("c.txt", frn: 2, size: 1),
        };

        var result = await Sut.UpsertBatchAsync(volumeId, batch);

        Assert.Equal(2, result.Written);
        Assert.Equal(1, result.SkippedNoFrn);
    }

    private static WalkedFile MakeEntry(string name, ulong? frn, long size, bool isDirectory = false) => new(
        CanonicalPath.FromCanonicalizedString(@"\\?\C:\test\" + name),
        name,
        frn,
        isDirectory,
        size,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        isDirectory ? FileAttributes.Directory : FileAttributes.Archive,
        Depth: 1);
}
