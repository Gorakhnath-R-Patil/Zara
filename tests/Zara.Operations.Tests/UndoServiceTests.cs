using Microsoft.Data.Sqlite;
using Zara.Core.Files;
using Zara.Core.Operations;
using Zara.Filesystem.Hashing;
using Zara.Filesystem.Paths;
using Zara.Filesystem.Shell;
using Zara.Storage;
using Zara.Storage.Journal;

namespace Zara.Operations.Tests;

/// <summary>
/// T35: <c>undo(op(fs)) == fs</c>, byte-identical, for Move/Copy/Rename —
/// exercised against real files, a real journal, and real
/// <c>IFileOperation</c> calls, not mocks. Written as a set of targeted
/// cases plus a genuinely randomized-content loop, rather than FsCheck
/// generators: each check here performs slow, stateful, real I/O (a real
/// Shell COM operation per file), which doesn't fit FsCheck's usual
/// "cheap pure function, hundreds of random inputs" model — see
/// TRACKER.md's decision log for the full reasoning. The property under
/// test is identical either way.
/// </summary>
public class UndoServiceTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly string _tempRoot;
    private SqliteConnectionFactory _connectionFactory = null!;
    private WriteQueue _writeQueue = null!;
    private OperationJournal _journal = null!;
    private ShellOperations _shellOperations = null!;
    private ContentHasher _contentHasher = null!;
    private UndoService _sut = null!;
    private PathCanonicalizer _canonicalizer = null!;

    public UndoServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-undo-test-{Guid.NewGuid():N}.db");
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-undo-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "src"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "dest"));
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
        _shellOperations = new ShellOperations();
        _contentHasher = new ContentHasher();
        _sut = new UndoService(_journal, _shellOperations, _contentHasher);
        _canonicalizer = new PathCanonicalizer();
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

    private CanonicalPath Canon(string realPath) => _canonicalizer.CanonicalizeExisting(realPath);

    /// <summary>Runs source-file → real shell operation → journal it →
    /// undo it, and returns (originalPath, originalContent) so the caller
    /// can assert byte-identical restoration.</summary>
    private async Task<(string OriginalPath, byte[] OriginalContent)> RunAndUndo(
        OperationKind kind, byte[] content, string fileName)
    {
        string srcDir = Path.Combine(_tempRoot, "src");
        string destDir = Path.Combine(_tempRoot, "dest");
        string sourcePath = Path.Combine(srcDir, fileName);
        File.WriteAllBytes(sourcePath, content);

        FileOperationBatch shellBatch;
        OperationPlan journalPlan;
        string plannedDestPath;

        switch (kind)
        {
            case OperationKind.Move:
                plannedDestPath = Path.Combine(destDir, fileName);
                shellBatch = new FileOperationBatch(FileOperationKind.Move, [new FileOperationItem(Canon(sourcePath), Canon(destDir))]);
                journalPlan = new OperationPlan(OperationKind.Move,
                    [new OperationItemPlan(Canon(sourcePath), CanonicalPath.FromCanonicalizedString(@"\\?\" + plannedDestPath))]);
                break;

            case OperationKind.Rename:
                string newName = "renamed-" + fileName;
                plannedDestPath = Path.Combine(srcDir, newName);
                shellBatch = new FileOperationBatch(FileOperationKind.Rename, [new FileOperationItem(Canon(sourcePath), NewName: newName)]);
                journalPlan = new OperationPlan(OperationKind.Rename,
                    [new OperationItemPlan(Canon(sourcePath), CanonicalPath.FromCanonicalizedString(@"\\?\" + plannedDestPath))]);
                break;

            case OperationKind.Copy:
                plannedDestPath = Path.Combine(destDir, fileName);
                shellBatch = new FileOperationBatch(FileOperationKind.Copy, [new FileOperationItem(Canon(sourcePath), Canon(destDir))]);
                journalPlan = new OperationPlan(OperationKind.Copy,
                    [new OperationItemPlan(Canon(sourcePath), CanonicalPath.FromCanonicalizedString(@"\\?\" + plannedDestPath))]);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var operationId = await _journal.BeginAsync(journalPlan);
        await _journal.MarkExecutingAsync(operationId);

        var outcome = await _shellOperations.ExecuteAsync(shellBatch);
        Assert.True(outcome.Succeeded, "The forward operation itself failed — nothing to undo.");

        for (int i = 0; i < outcome.Items.Count; i++)
        {
            var item = outcome.Items[i];
            await _journal.RecordItemAsync(operationId, new OperationItemOutcome(i, OperationItemStatus.Completed, item.ResultPath));
        }

        await _journal.CompleteAsync(operationId, OperationStatus.Completed);

        var undoResult = await _sut.UndoAsync(operationId);
        Assert.True(undoResult.Succeeded, $"Undo failed: {string.Join("; ", undoResult.UnrecoverableReasons)}");

        return (sourcePath, content);
    }

    [Fact]
    public async Task UndoMove_RestoresByteIdenticalContentAtTheOriginalPath()
    {
        byte[] content = "move me and bring me back"u8.ToArray();

        var (originalPath, originalContent) = await RunAndUndo(OperationKind.Move, content, "move-test.txt");

        Assert.True(File.Exists(originalPath));
        Assert.Equal(originalContent, File.ReadAllBytes(originalPath));
    }

    [Fact]
    public async Task UndoRename_RestoresOriginalNameAndByteIdenticalContent()
    {
        byte[] content = "rename me and bring me back"u8.ToArray();

        var (originalPath, originalContent) = await RunAndUndo(OperationKind.Rename, content, "rename-test.txt");

        Assert.True(File.Exists(originalPath));
        Assert.Equal(originalContent, File.ReadAllBytes(originalPath));
        Assert.False(File.Exists(Path.Combine(_tempRoot, "src", "renamed-rename-test.txt")));
    }

    [Fact]
    public async Task UndoCopy_DeletesTheCopyAndLeavesTheOriginalByteIdentical()
    {
        byte[] content = "copy me, then undo should delete the copy"u8.ToArray();

        var (originalPath, originalContent) = await RunAndUndo(OperationKind.Copy, content, "copy-test.txt");

        // The ORIGINAL was never touched by a copy — still there, unchanged.
        Assert.True(File.Exists(originalPath));
        Assert.Equal(originalContent, File.ReadAllBytes(originalPath));
        // The COPY is gone.
        Assert.False(File.Exists(Path.Combine(_tempRoot, "dest", "copy-test.txt")));
    }

    [Fact]
    public async Task UndoMove_RandomizedContentAcrossManyTrials_IsAlwaysByteIdentical()
    {
        // The closest thing to a real property check here: many different
        // random byte payloads, each independently round-tripped through a
        // real move + undo, asserting the core property every time.
        var random = new Random(20260809);

        for (int trial = 0; trial < 15; trial++)
        {
            byte[] content = new byte[random.Next(0, 5000)];
            random.NextBytes(content);

            var (originalPath, originalContent) = await RunAndUndo(OperationKind.Move, content, $"trial-{trial}.bin");

            Assert.True(File.Exists(originalPath), $"Trial {trial}: file missing after undo.");
            Assert.Equal(originalContent, File.ReadAllBytes(originalPath));
        }
    }

    [Fact]
    public async Task UndoCopy_WhenTheCopyWasModifiedSinceCopying_RefusesToDeleteIt()
    {
        string srcDir = Path.Combine(_tempRoot, "src");
        string destDir = Path.Combine(_tempRoot, "dest");
        string sourcePath = Path.Combine(srcDir, "verify-test.txt");
        File.WriteAllText(sourcePath, "original content");

        var batch = new FileOperationBatch(FileOperationKind.Copy, [new FileOperationItem(Canon(sourcePath), Canon(destDir))]);
        string destPath = Path.Combine(destDir, "verify-test.txt");

        var plan = new OperationPlan(OperationKind.Copy,
            [new OperationItemPlan(Canon(sourcePath), CanonicalPath.FromCanonicalizedString(@"\\?\" + destPath))]);
        var operationId = await _journal.BeginAsync(plan);
        await _journal.MarkExecutingAsync(operationId);

        var outcome = await _shellOperations.ExecuteAsync(batch);
        await _journal.RecordItemAsync(operationId, new OperationItemOutcome(0, OperationItemStatus.Completed, outcome.Items[0].ResultPath));
        await _journal.CompleteAsync(operationId, OperationStatus.Completed);

        // The copy is modified AFTER being copied — undo must not delete it,
        // per §19.1's hash-verification requirement.
        File.WriteAllText(destPath, "modified after copying — this is now different content");

        var undoResult = await _sut.UndoAsync(operationId);

        Assert.False(undoResult.Succeeded);
        Assert.True(File.Exists(destPath), "The modified copy must NOT have been deleted.");
        Assert.Equal("modified after copying — this is now different content", File.ReadAllText(destPath));
    }

    [Fact]
    public async Task UndoAsync_Delete_ReturnsAnHonestFailure_NotASilentNoOp()
    {
        // Not created on disk — CanonicalizeExisting requires the file to
        // actually exist (it opens a real handle), which this deliberately
        // nonexistent-file scenario can't satisfy; build the path directly.
        var missingPath = CanonicalPath.FromCanonicalizedString(@"\\?\" + Path.Combine(_tempRoot, "src", "whatever.txt"));
        var plan = new OperationPlan(OperationKind.Delete, [new OperationItemPlan(missingPath)]);
        var operationId = await _journal.BeginAsync(plan);
        await _journal.CompleteAsync(operationId, OperationStatus.Completed);

        var result = await _sut.UndoAsync(operationId);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.UnrecoverableReasons);
    }

    [Fact]
    public async Task UndoAsync_NothingCompleted_ReturnsSuccessWithNothingToDo()
    {
        // The operation was journaled but never actually executed (still
        // 'pending' items) — undoing "nothing that happened" is trivially
        // successful, not an error. Not created on disk — see the comment
        // in UndoAsync_Delete_ReturnsAnHonestFailure_NotASilentNoOp.
        var missingPath = CanonicalPath.FromCanonicalizedString(@"\\?\" + Path.Combine(_tempRoot, "src", "never-moved.txt"));
        var plan = new OperationPlan(OperationKind.Move, [new OperationItemPlan(missingPath)]);
        var operationId = await _journal.BeginAsync(plan);

        var result = await _sut.UndoAsync(operationId);

        Assert.True(result.Succeeded);
        Assert.Empty(result.UnrecoverableReasons);
    }

    [Fact]
    public async Task UndoAsync_MarksTheOriginalOperationUndone()
    {
        var (_, _, operationId) = await RunAndUndoTrackOperation();

        var record = await _journal.GetAsync(operationId);

        Assert.Equal(OperationStatus.Undone, record!.Status);
    }

    private async Task<(string Path, byte[] Content, Guid OperationId)> RunAndUndoTrackOperation()
    {
        string srcDir = Path.Combine(_tempRoot, "src");
        string destDir = Path.Combine(_tempRoot, "dest");
        string sourcePath = Path.Combine(srcDir, "tracked.txt");
        byte[] content = "tracked"u8.ToArray();
        File.WriteAllBytes(sourcePath, content);

        var batch = new FileOperationBatch(FileOperationKind.Move, [new FileOperationItem(Canon(sourcePath), Canon(destDir))]);
        string destPath = Path.Combine(destDir, "tracked.txt");
        var plan = new OperationPlan(OperationKind.Move,
            [new OperationItemPlan(Canon(sourcePath), CanonicalPath.FromCanonicalizedString(@"\\?\" + destPath))]);

        var operationId = await _journal.BeginAsync(plan);
        await _journal.MarkExecutingAsync(operationId);
        var outcome = await _shellOperations.ExecuteAsync(batch);
        await _journal.RecordItemAsync(operationId, new OperationItemOutcome(0, OperationItemStatus.Completed, outcome.Items[0].ResultPath));
        await _journal.CompleteAsync(operationId, OperationStatus.Completed);

        await _sut.UndoAsync(operationId);
        return (sourcePath, content, operationId);
    }
}
