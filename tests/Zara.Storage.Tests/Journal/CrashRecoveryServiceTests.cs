using Microsoft.Data.Sqlite;
using Zara.Core.Files;
using Zara.Core.Operations;
using Zara.Storage.Journal;

namespace Zara.Storage.Tests.Journal;

/// <summary>Uses real temp files to simulate each crash scenario §19.4
/// describes — this is exactly the kind of logic that's easy to get subtly
/// wrong without testing against the real filesystem it reasons about.</summary>
public class CrashRecoveryServiceTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly string _tempRoot;
    private SqliteConnectionFactory _connectionFactory = null!;
    private WriteQueue _writeQueue = null!;
    private OperationJournal _journal = null!;
    private CrashRecoveryService _sut = null!;

    public CrashRecoveryServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-crashrecovery-test-{Guid.NewGuid():N}.db");
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-crashrecovery-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public Task InitializeAsync()
    {
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        using (var connection = _connectionFactory.CreateConnection())
        {
            new MigrationRunner().MigrateToLatest(connection);
        }
        _writeQueue = new WriteQueue(_connectionFactory);
        _journal = new OperationJournal(_writeQueue, _connectionFactory);
        _sut = new CrashRecoveryService(_journal);
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

        try { Directory.Delete(_tempRoot, recursive: true); } catch (IOException) { }
    }

    private string RealPath(string name) => Path.Combine(_tempRoot, name);
    private static CanonicalPath Canon(string realPath) => CanonicalPath.FromCanonicalizedString(@"\\?\" + realPath);

    [Fact]
    public async Task ReconcileInterruptedOperationsAsync_CleanState_ReconcilesNothing()
    {
        var id = await _journal.BeginAsync(new OperationPlan(OperationKind.Move, [new OperationItemPlan(Canon(RealPath("a.txt")))]));
        await _journal.CompleteAsync(id, OperationStatus.Completed); // never crashed

        var reconciled = await _sut.ReconcileInterruptedOperationsAsync();

        Assert.Empty(reconciled);
    }

    [Fact]
    public async Task ReconcileInterruptedOperationsAsync_Move_SourceGoneDestPresent_InferredCompleted()
    {
        string source = RealPath("moved-source.txt");
        string dest = RealPath("moved-dest.txt");
        File.WriteAllText(dest, "already moved"); // simulates: the move finished, then the process died

        var plan = new OperationPlan(OperationKind.Move, [new OperationItemPlan(Canon(source), Canon(dest))]);
        var id = await _journal.BeginAsync(plan);
        await _journal.MarkExecutingAsync(id); // ...and never completed — simulated crash

        var reconciled = await _sut.ReconcileInterruptedOperationsAsync();

        Assert.Contains(id, reconciled);
        var items = await _journal.GetItemsAsync(id);
        Assert.Equal(OperationItemStatus.Completed, items[0].Status);
        var operation = await _journal.GetAsync(id);
        Assert.Equal(OperationStatus.Partial, operation!.Status);
    }

    [Fact]
    public async Task ReconcileInterruptedOperationsAsync_Move_SourcePresentDestAbsent_InferredPending()
    {
        string source = RealPath("never-moved.txt");
        File.WriteAllText(source, "still here");
        string dest = RealPath("never-created.txt"); // never written

        var plan = new OperationPlan(OperationKind.Move, [new OperationItemPlan(Canon(source), Canon(dest))]);
        var id = await _journal.BeginAsync(plan);
        await _journal.MarkExecutingAsync(id);

        await _sut.ReconcileInterruptedOperationsAsync();

        var items = await _journal.GetItemsAsync(id);
        Assert.Equal(OperationItemStatus.Pending, items[0].Status);
    }

    [Fact]
    public async Task ReconcileInterruptedOperationsAsync_Move_BothPresent_InferredUnrecoverable()
    {
        // Ambiguous per §19.4: could be a completed op whose source deletion
        // step is what actually crashed, or something else entirely — the
        // rule is "don't guess", so this must NOT be silently marked completed.
        string source = RealPath("both-a.txt");
        string dest = RealPath("both-b.txt");
        File.WriteAllText(source, "x");
        File.WriteAllText(dest, "y");

        var plan = new OperationPlan(OperationKind.Move, [new OperationItemPlan(Canon(source), Canon(dest))]);
        var id = await _journal.BeginAsync(plan);
        await _journal.MarkExecutingAsync(id);

        await _sut.ReconcileInterruptedOperationsAsync();

        var items = await _journal.GetItemsAsync(id);
        Assert.Equal(OperationItemStatus.Unrecoverable, items[0].Status);
    }

    [Fact]
    public async Task ReconcileInterruptedOperationsAsync_Copy_DestPresent_InferredCompleted()
    {
        string source = RealPath("copy-source.txt");
        string dest = RealPath("copy-dest.txt");
        File.WriteAllText(source, "original"); // copy never removes the source
        File.WriteAllText(dest, "copy");

        var plan = new OperationPlan(OperationKind.Copy, [new OperationItemPlan(Canon(source), Canon(dest))]);
        var id = await _journal.BeginAsync(plan);
        await _journal.MarkExecutingAsync(id);

        await _sut.ReconcileInterruptedOperationsAsync();

        var items = await _journal.GetItemsAsync(id);
        Assert.Equal(OperationItemStatus.Completed, items[0].Status);
    }

    [Fact]
    public async Task ReconcileInterruptedOperationsAsync_Copy_DestAbsent_InferredPending()
    {
        string source = RealPath("copy-source2.txt");
        File.WriteAllText(source, "original");
        string dest = RealPath("copy-dest2.txt"); // never created

        var plan = new OperationPlan(OperationKind.Copy, [new OperationItemPlan(Canon(source), Canon(dest))]);
        var id = await _journal.BeginAsync(plan);
        await _journal.MarkExecutingAsync(id);

        await _sut.ReconcileInterruptedOperationsAsync();

        var items = await _journal.GetItemsAsync(id);
        Assert.Equal(OperationItemStatus.Pending, items[0].Status);
    }

    [Fact]
    public async Task ReconcileInterruptedOperationsAsync_Delete_SourceGone_InferredCompleted()
    {
        string source = RealPath("deleted-already.txt"); // never created — simulates it having been deleted

        var plan = new OperationPlan(OperationKind.Delete, [new OperationItemPlan(Canon(source))]);
        var id = await _journal.BeginAsync(plan);
        await _journal.MarkExecutingAsync(id);

        await _sut.ReconcileInterruptedOperationsAsync();

        var items = await _journal.GetItemsAsync(id);
        Assert.Equal(OperationItemStatus.Completed, items[0].Status);
    }

    [Fact]
    public async Task ReconcileInterruptedOperationsAsync_Delete_SourceStillPresent_InferredPending()
    {
        string source = RealPath("not-deleted-yet.txt");
        File.WriteAllText(source, "still here");

        var plan = new OperationPlan(OperationKind.Delete, [new OperationItemPlan(Canon(source))]);
        var id = await _journal.BeginAsync(plan);
        await _journal.MarkExecutingAsync(id);

        await _sut.ReconcileInterruptedOperationsAsync();

        var items = await _journal.GetItemsAsync(id);
        Assert.Equal(OperationItemStatus.Pending, items[0].Status);
    }

    [Fact]
    public async Task ReconcileInterruptedOperationsAsync_AlreadyResolvedItems_AreLeftAlone()
    {
        string source1 = RealPath("resolved.txt");
        File.WriteAllText(source1, "x");
        string source2 = RealPath("unresolved.txt");
        File.WriteAllText(source2, "y");

        var plan = new OperationPlan(OperationKind.Delete,
            [new OperationItemPlan(Canon(source1)), new OperationItemPlan(Canon(source2))]);
        var id = await _journal.BeginAsync(plan);
        await _journal.MarkExecutingAsync(id);

        // Item 0 was already recorded as Failed BEFORE the crash — reconciliation
        // must not touch it, since it's not Pending.
        await _journal.RecordItemAsync(id, new OperationItemOutcome(0, OperationItemStatus.Failed, ErrorCode: "ACCESS_DENIED"));

        await _sut.ReconcileInterruptedOperationsAsync();

        var items = await _journal.GetItemsAsync(id);
        Assert.Equal(OperationItemStatus.Failed, items[0].Status); // untouched
        Assert.Equal("ACCESS_DENIED", items[0].ErrorCode);
        Assert.Equal(OperationItemStatus.Pending, items[1].Status); // source2 still exists -> pending
    }

    [Fact]
    public async Task ReconcileInterruptedOperationsAsync_MarksTheOperationPartial_EvenIfEveryItemResolvesCompleted()
    {
        string source = RealPath("fully-done-source.txt");
        string dest = RealPath("fully-done-dest.txt");
        File.WriteAllText(dest, "done");

        var plan = new OperationPlan(OperationKind.Move, [new OperationItemPlan(Canon(source), Canon(dest))]);
        var id = await _journal.BeginAsync(plan);
        await _journal.MarkExecutingAsync(id);

        await _sut.ReconcileInterruptedOperationsAsync();

        // Never silently promoted back to "Completed" — §19.4: a crashed
        // operation always needs a human to have seen it, even if every
        // item turned out fine.
        var operation = await _journal.GetAsync(id);
        Assert.Equal(OperationStatus.Partial, operation!.Status);
    }

    [Fact]
    public async Task ReconcileInterruptedOperationsAsync_MultipleInterruptedOperations_AllReconciled()
    {
        var id1 = await _journal.BeginAsync(new OperationPlan(OperationKind.Delete, [new OperationItemPlan(Canon(RealPath("m1.txt")))]));
        await _journal.MarkExecutingAsync(id1);
        var id2 = await _journal.BeginAsync(new OperationPlan(OperationKind.Delete, [new OperationItemPlan(Canon(RealPath("m2.txt")))]));
        await _journal.MarkExecutingAsync(id2);

        var reconciled = await _sut.ReconcileInterruptedOperationsAsync();

        Assert.Equal(2, reconciled.Count);
        Assert.Contains(id1, reconciled);
        Assert.Contains(id2, reconciled);
    }
}
