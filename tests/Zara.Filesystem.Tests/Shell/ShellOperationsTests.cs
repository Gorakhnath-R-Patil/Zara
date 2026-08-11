using Zara.Filesystem.Paths;
using Zara.Filesystem.Shell;

namespace Zara.Filesystem.Tests.Shell;

/// <summary>
/// Real file operations against a real temp directory — this is the part of
/// T31 that actually matters. A mocked <c>IFileOperation</c> would prove
/// nothing about whether the Vanara wiring (flags, event args, ShellItem/
/// ShellFolder construction) is actually correct.
/// </summary>
public class ShellOperationsTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly PathCanonicalizer _canonicalizer = new();
    private readonly ShellOperations _sut = new();

    public ShellOperationsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-shellops-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private Zara.Core.Files.CanonicalPath Canonical(string path) => _canonicalizer.CanonicalizeExisting(path);

    private string WriteFile(string relativeName, string content = "content")
    {
        string path = Path.Combine(_tempRoot, relativeName);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task ExecuteAsync_Copy_LeavesSourceAndCreatesDestination()
    {
        string sourcePath = WriteFile("source.txt", "hello");
        string destDir = Path.Combine(_tempRoot, "dest");
        Directory.CreateDirectory(destDir);

        var batch = new FileOperationBatch(FileOperationKind.Copy,
            [new FileOperationItem(Canonical(sourcePath), Canonical(destDir))]);

        var outcome = await _sut.ExecuteAsync(batch);

        Assert.True(outcome.Succeeded);
        Assert.True(File.Exists(sourcePath));
        Assert.True(File.Exists(Path.Combine(destDir, "source.txt")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(destDir, "source.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_Copy_WithNewName_RenamesAtDestination()
    {
        string sourcePath = WriteFile("original.txt", "data");
        string destDir = Path.Combine(_tempRoot, "dest");
        Directory.CreateDirectory(destDir);

        var batch = new FileOperationBatch(FileOperationKind.Copy,
            [new FileOperationItem(Canonical(sourcePath), Canonical(destDir), NewName: "renamed.txt")]);

        var outcome = await _sut.ExecuteAsync(batch);

        Assert.True(outcome.Succeeded);
        Assert.True(File.Exists(Path.Combine(destDir, "renamed.txt")));
        Assert.False(File.Exists(Path.Combine(destDir, "original.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_Move_RemovesSourceAndCreatesDestination()
    {
        string sourcePath = WriteFile("move-me.txt", "payload");
        string destDir = Path.Combine(_tempRoot, "dest");
        Directory.CreateDirectory(destDir);

        var batch = new FileOperationBatch(FileOperationKind.Move,
            [new FileOperationItem(Canonical(sourcePath), Canonical(destDir))]);

        var outcome = await _sut.ExecuteAsync(batch);

        Assert.True(outcome.Succeeded);
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(Path.Combine(destDir, "move-me.txt")));
        Assert.Equal("payload", File.ReadAllText(Path.Combine(destDir, "move-me.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_Rename_ChangesNameInPlace()
    {
        string sourcePath = WriteFile("before.txt", "x");

        var batch = new FileOperationBatch(FileOperationKind.Rename,
            [new FileOperationItem(Canonical(sourcePath), NewName: "after.txt")]);

        var outcome = await _sut.ExecuteAsync(batch);

        Assert.True(outcome.Succeeded);
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "after.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_Delete_RemovesFileFromOriginalLocation()
    {
        // Goes to the Recycle Bin (AllowUndo flag), not permanent delete —
        // this test verifies the file is gone from its original path, which
        // is true either way; it does not attempt to verify Recycle Bin
        // restoration (a separate, more involved capability — see
        // FileOperationItemOutcome's remarks on why ResultPath is null here).
        string path = WriteFile("delete-me.txt");

        var batch = new FileOperationBatch(FileOperationKind.Delete, [new FileOperationItem(Canonical(path))]);

        var outcome = await _sut.ExecuteAsync(batch);

        Assert.True(outcome.Succeeded);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ExecuteAsync_MultipleItemsInOneBatch_AllSucceed()
    {
        string a = WriteFile("a.txt");
        string b = WriteFile("b.txt");
        string destDir = Path.Combine(_tempRoot, "dest");
        Directory.CreateDirectory(destDir);

        var batch = new FileOperationBatch(FileOperationKind.Move,
            [new FileOperationItem(Canonical(a), Canonical(destDir)), new FileOperationItem(Canonical(b), Canonical(destDir))]);

        var outcome = await _sut.ExecuteAsync(batch);

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Items.Count);
        Assert.True(File.Exists(Path.Combine(destDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(destDir, "b.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgressForEachItem()
    {
        string a = WriteFile("p1.txt");
        string b = WriteFile("p2.txt");
        string destDir = Path.Combine(_tempRoot, "dest");
        Directory.CreateDirectory(destDir);

        var reports = new List<OperationProgress>();
        var progress = new Progress<OperationProgress>(reports.Add);

        var batch = new FileOperationBatch(FileOperationKind.Move,
            [new FileOperationItem(Canonical(a), Canonical(destDir)), new FileOperationItem(Canonical(b), Canonical(destDir))]);

        await _sut.ExecuteAsync(batch, progress);

        // Progress is reported via a captured SynchronizationContext (Progress<T>),
        // which in a test host may deliver asynchronously — give it a moment.
        for (int i = 0; i < 20 && reports.Count < 2; i++)
        {
            await Task.Delay(50);
        }

        Assert.True(reports.Count >= 1, "Expected at least one progress report.");
        Assert.Equal(2, reports[^1].ItemsTotal);
    }

    [Fact]
    public async Task ExecuteAsync_OutcomeIncludesResultPathForSuccessfulMove()
    {
        string sourcePath = WriteFile("tracked.txt");
        string destDir = Path.Combine(_tempRoot, "dest");
        Directory.CreateDirectory(destDir);

        var batch = new FileOperationBatch(FileOperationKind.Move,
            [new FileOperationItem(Canonical(sourcePath), Canonical(destDir))]);

        var outcome = await _sut.ExecuteAsync(batch);

        var item = Assert.Single(outcome.Items);
        Assert.NotNull(item.ResultPath);
        Assert.Contains("tracked.txt", item.ResultPath!.Value.Value);
    }
}
