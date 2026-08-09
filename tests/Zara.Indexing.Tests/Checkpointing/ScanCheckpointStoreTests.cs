using Zara.Indexing.Checkpointing;

namespace Zara.Indexing.Tests.Checkpointing;

public class ScanCheckpointStoreTests : SqliteTestFixture
{
    private ScanCheckpointStore Sut => new(ConnectionFactory, WriteQueue);

    [Fact]
    public async Task GetCompletedChildrenAsync_NoCheckpoints_ReturnsEmpty()
    {
        long volumeId = InsertVolume();

        var completed = await Sut.GetCompletedChildrenAsync(volumeId, @"\\?\C:\root");

        Assert.Empty(completed);
    }

    [Fact]
    public async Task MarkChildCompletedAsync_ThenGet_ReturnsIt()
    {
        long volumeId = InsertVolume();

        await Sut.MarkChildCompletedAsync(volumeId, @"\\?\C:\root", "sub-a");
        var completed = await Sut.GetCompletedChildrenAsync(volumeId, @"\\?\C:\root");

        Assert.Contains("sub-a", completed);
    }

    [Fact]
    public async Task MarkChildCompletedAsync_IsIdempotent()
    {
        long volumeId = InsertVolume();

        await Sut.MarkChildCompletedAsync(volumeId, @"\\?\C:\root", "sub-a");
        var exception = await Record.ExceptionAsync(() => Sut.MarkChildCompletedAsync(volumeId, @"\\?\C:\root", "sub-a"));

        Assert.Null(exception);
        var completed = await Sut.GetCompletedChildrenAsync(volumeId, @"\\?\C:\root");
        Assert.Single(completed);
    }

    [Fact]
    public async Task Checkpoints_AreScopedPerRoot_NotJustPerVolume()
    {
        long volumeId = InsertVolume();

        await Sut.MarkChildCompletedAsync(volumeId, @"\\?\C:\root-a", "sub");
        var completedForRootB = await Sut.GetCompletedChildrenAsync(volumeId, @"\\?\C:\root-b");

        Assert.Empty(completedForRootB);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllCheckpointsForThatRoot()
    {
        long volumeId = InsertVolume();
        await Sut.MarkChildCompletedAsync(volumeId, @"\\?\C:\root", "sub-a");
        await Sut.MarkChildCompletedAsync(volumeId, @"\\?\C:\root", "sub-b");

        await Sut.ClearAsync(volumeId, @"\\?\C:\root");

        var completed = await Sut.GetCompletedChildrenAsync(volumeId, @"\\?\C:\root");
        Assert.Empty(completed);
    }
}
