using Microsoft.Data.Sqlite;
using Zara.Core.Files;
using Zara.Core.Operations;
using Zara.Storage.Journal;

namespace Zara.Storage.Tests.Journal;

public class OperationJournalTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private SqliteConnectionFactory _connectionFactory = null!;
    private WriteQueue _writeQueue = null!;
    private OperationJournal _sut = null!;

    public OperationJournalTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-journal-test-{Guid.NewGuid():N}.db");
    }

    public Task InitializeAsync()
    {
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        using (var connection = _connectionFactory.CreateConnection())
        {
            new MigrationRunner().MigrateToLatest(connection);
        }
        _writeQueue = new WriteQueue(_connectionFactory);
        _sut = new OperationJournal(_writeQueue, _connectionFactory);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _writeQueue.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    private static CanonicalPath Path_(string p) => CanonicalPath.FromCanonicalizedString(@"\\?\" + p);

    private static OperationPlan MakePlan(OperationKind kind = OperationKind.Move, int itemCount = 2) => new(
        kind,
        Enumerable.Range(0, itemCount)
            .Select(i => new OperationItemPlan(Path_($@"C:\src\file{i}.txt"), Path_($@"C:\dest\file{i}.txt")))
            .ToList(),
        RiskClass: "medium",
        ConfirmedByUser: true,
        UserRequest: "move these files");

    [Fact]
    public async Task BeginAsync_CreatesAPlannedOperationWithAllItems()
    {
        var plan = MakePlan(itemCount: 3);

        var id = await _sut.BeginAsync(plan);
        var record = await _sut.GetAsync(id);
        var items = await _sut.GetItemsAsync(id);

        Assert.NotNull(record);
        Assert.Equal(OperationStatus.Planned, record.Status);
        Assert.Equal(OperationKind.Move, record.Kind);
        Assert.Equal(3, record.ItemCount);
        Assert.Equal("medium", record.RiskClass);
        Assert.True(record.ConfirmedByUser);
        Assert.Equal("move these files", record.UserRequest);

        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal(OperationItemStatus.Pending, i.Status));
    }

    [Fact]
    public async Task BeginAsync_GeneratesAUniqueIdPerCall()
    {
        var id1 = await _sut.BeginAsync(MakePlan());
        var id2 = await _sut.BeginAsync(MakePlan());

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task MarkExecutingAsync_UpdatesStatus()
    {
        var id = await _sut.BeginAsync(MakePlan());

        await _sut.MarkExecutingAsync(id);
        var record = await _sut.GetAsync(id);

        Assert.Equal(OperationStatus.Executing, record!.Status);
    }

    [Fact]
    public async Task RecordItemAsync_UpdatesItemStatus()
    {
        var id = await _sut.BeginAsync(MakePlan(itemCount: 2));
        await _sut.MarkExecutingAsync(id);

        await _sut.RecordItemAsync(id, new OperationItemOutcome(0, OperationItemStatus.Completed));
        await _sut.RecordItemAsync(id, new OperationItemOutcome(1, OperationItemStatus.Failed, ErrorCode: "SHARING_VIOLATION"));

        var items = await _sut.GetItemsAsync(id);

        Assert.Equal(OperationItemStatus.Completed, items[0].Status);
        Assert.Equal(OperationItemStatus.Failed, items[1].Status);
        Assert.Equal("SHARING_VIOLATION", items[1].ErrorCode);
    }

    [Fact]
    public async Task RecordItemAsync_WithNoExplicitDestPath_KeepsThePlannedOne()
    {
        var id = await _sut.BeginAsync(MakePlan(itemCount: 1));

        await _sut.RecordItemAsync(id, new OperationItemOutcome(0, OperationItemStatus.Completed)); // DestPath omitted

        var items = await _sut.GetItemsAsync(id);
        Assert.Equal(@"\\?\C:\dest\file0.txt", items[0].DestPath);
    }

    [Fact]
    public async Task RecordItemAsync_WithExplicitDestPath_OverwritesThePlannedOne()
    {
        var id = await _sut.BeginAsync(MakePlan(itemCount: 1));

        await _sut.RecordItemAsync(id, new OperationItemOutcome(0, OperationItemStatus.Completed, DestPath: Path_(@"C:\dest\file0 (2).txt")));

        var items = await _sut.GetItemsAsync(id);
        Assert.Equal(@"\\?\C:\dest\file0 (2).txt", items[0].DestPath);
    }

    [Fact]
    public async Task RecordItemAsync_RecordsRecycleId_ForDeletes()
    {
        var plan = new OperationPlan(OperationKind.Delete, [new OperationItemPlan(Path_(@"C:\gone.txt"))]);
        var id = await _sut.BeginAsync(plan);

        await _sut.RecordItemAsync(id, new OperationItemOutcome(0, OperationItemStatus.Completed, RecycleId: "recycle-bin-item-123"));

        var items = await _sut.GetItemsAsync(id);
        Assert.Equal("recycle-bin-item-123", items[0].RecycleId);
    }

    [Fact]
    public async Task CompleteAsync_SetsStatusAndCompletedUtc()
    {
        var id = await _sut.BeginAsync(MakePlan());

        await _sut.CompleteAsync(id, OperationStatus.Completed);
        var record = await _sut.GetAsync(id);

        Assert.Equal(OperationStatus.Completed, record!.Status);
        Assert.NotNull(record.CompletedUtc);
    }

    [Fact]
    public async Task CompleteAsync_PartialSuccess_RecordsPartialStatus()
    {
        var id = await _sut.BeginAsync(MakePlan());

        await _sut.CompleteAsync(id, OperationStatus.Partial);
        var record = await _sut.GetAsync(id);

        Assert.Equal(OperationStatus.Partial, record!.Status);
    }

    [Fact]
    public async Task GetInterruptedAsync_FindsOnlyExecutingOperations()
    {
        var executingId = await _sut.BeginAsync(MakePlan());
        await _sut.MarkExecutingAsync(executingId);

        var completedId = await _sut.BeginAsync(MakePlan());
        await _sut.CompleteAsync(completedId, OperationStatus.Completed);

        var plannedId = await _sut.BeginAsync(MakePlan()); // never marked executing

        var interrupted = await _sut.GetInterruptedAsync();

        Assert.Single(interrupted);
        Assert.Equal(executingId, interrupted[0].Id);
    }

    [Fact]
    public async Task GetInterruptedAsync_CleanState_ReturnsEmpty()
    {
        var id = await _sut.BeginAsync(MakePlan());
        await _sut.CompleteAsync(id, OperationStatus.Completed);

        var interrupted = await _sut.GetInterruptedAsync();

        Assert.Empty(interrupted);
    }

    [Fact]
    public async Task GetUndoableAsync_ReturnsCompletedAndPartial_NotPlannedOrExecuting()
    {
        var completedId = await _sut.BeginAsync(MakePlan());
        await _sut.CompleteAsync(completedId, OperationStatus.Completed);

        var partialId = await _sut.BeginAsync(MakePlan());
        await _sut.CompleteAsync(partialId, OperationStatus.Partial);

        var plannedId = await _sut.BeginAsync(MakePlan());

        var executingId = await _sut.BeginAsync(MakePlan());
        await _sut.MarkExecutingAsync(executingId);

        var undoable = await _sut.GetUndoableAsync(limit: 10);

        Assert.Equal(2, undoable.Count);
        Assert.Contains(undoable, o => o.Id == completedId);
        Assert.Contains(undoable, o => o.Id == partialId);
    }

    [Fact]
    public async Task GetUndoableAsync_OrdersNewestFirst()
    {
        var first = await _sut.BeginAsync(MakePlan());
        await _sut.CompleteAsync(first, OperationStatus.Completed);
        var second = await _sut.BeginAsync(MakePlan());
        await _sut.CompleteAsync(second, OperationStatus.Completed);

        var undoable = await _sut.GetUndoableAsync(limit: 10);

        Assert.Equal(second, undoable[0].Id);
        Assert.Equal(first, undoable[1].Id);
    }

    [Fact]
    public async Task GetUndoableAsync_RespectsLimit()
    {
        for (int i = 0; i < 5; i++)
        {
            var id = await _sut.BeginAsync(MakePlan());
            await _sut.CompleteAsync(id, OperationStatus.Completed);
        }

        var undoable = await _sut.GetUndoableAsync(limit: 2);

        Assert.Equal(2, undoable.Count);
    }

    [Fact]
    public async Task GetAsync_NonexistentId_ReturnsNull()
    {
        var record = await _sut.GetAsync(Guid.NewGuid());

        Assert.Null(record);
    }
}
