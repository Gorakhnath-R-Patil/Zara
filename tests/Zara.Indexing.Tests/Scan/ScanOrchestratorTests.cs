using Zara.Core.Files;
using Zara.Filesystem.Enumeration;
using Zara.Filesystem.Paths;
using Zara.Indexing.Checkpointing;
using Zara.Indexing.Scan;
using Zara.Indexing.Writing;
using Zara.Volumes.Fallback;
using Zara.Volumes.Skip;

namespace Zara.Indexing.Tests.Scan;

public class ScanOrchestratorTests : SqliteTestFixture, IDisposable
{
    private readonly string _tempRoot;
    private readonly PathCanonicalizer _canonicalizer = new();
    private readonly NtDirectoryEnumerator _directoryEnumerator = new();

    public ScanOrchestratorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-scanorch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch (IOException) { }
    }

    private ScanOrchestrator MakeOrchestrator(IWalkScanner? walkScanner = null) => new(
        _directoryEnumerator,
        walkScanner ?? new WalkScanner(_directoryEnumerator),
        new FileIndexWriter(WriteQueue),
        new ScanCheckpointStore(ConnectionFactory, WriteQueue),
        new NoOpSkipList()); // see WalkScannerTests' PassthroughOptions remarks: the real
                              // DefaultSkipList excludes the OS Temp folder, which is where
                              // these test fixtures live.

    private void BuildTree(int childDirCount, int filesPerChildDir, int rootFileCount)
    {
        for (int i = 0; i < rootFileCount; i++)
        {
            File.WriteAllText(Path.Combine(_tempRoot, $"root-file-{i}.txt"), "1");
        }

        for (int c = 0; c < childDirCount; c++)
        {
            string childDir = Path.Combine(_tempRoot, $"child-{c}");
            Directory.CreateDirectory(childDir);
            for (int f = 0; f < filesPerChildDir; f++)
            {
                File.WriteAllText(Path.Combine(childDir, $"file-{f}.txt"), "1");
            }
        }
    }

    private CanonicalPath Root() => _canonicalizer.CanonicalizeExisting(_tempRoot);

    [Fact]
    public async Task ScanAsync_IndexesEveryFileAndDirectory()
    {
        BuildTree(childDirCount: 3, filesPerChildDir: 4, rootFileCount: 2);
        long volumeId = InsertVolume();

        var result = await MakeOrchestrator().ScanAsync(volumeId, Root());

        // 2 root files + 3 child dirs + (3*4) files inside them = 17 rows.
        Assert.Equal(17, result.FilesWritten);
        Assert.Equal(0, result.FilesSkipped);
        Assert.Equal(17, CountFiles());
    }

    [Fact]
    public async Task ScanAsync_ClearsCheckpointsOnceFullyComplete()
    {
        BuildTree(childDirCount: 2, filesPerChildDir: 1, rootFileCount: 0);
        long volumeId = InsertVolume();

        await MakeOrchestrator().ScanAsync(volumeId, Root());

        var checkpoints = new ScanCheckpointStore(ConnectionFactory, WriteQueue);
        var remaining = await checkpoints.GetCompletedChildrenAsync(volumeId, Root().Value);
        Assert.Empty(remaining); // cleared, per IScanCheckpointStore.ClearAsync's contract
    }

    [Fact]
    public async Task ScanAsync_EmptyDirectory_CompletesWithZeroWrites()
    {
        long volumeId = InsertVolume();

        var result = await MakeOrchestrator().ScanAsync(volumeId, Root());

        Assert.Equal(0, result.FilesWritten);
        Assert.Equal(0, result.TopLevelChildrenTotal);
    }

    [Fact]
    public async Task ScanAsync_RunTwiceWithNothingChanged_ProducesNoDuplicateRows()
    {
        BuildTree(childDirCount: 2, filesPerChildDir: 3, rootFileCount: 1);
        long volumeId = InsertVolume();
        var orchestrator = MakeOrchestrator();

        await orchestrator.ScanAsync(volumeId, Root());
        long afterFirst = CountFiles();
        await orchestrator.ScanAsync(volumeId, Root());
        long afterSecond = CountFiles();

        Assert.Equal(afterFirst, afterSecond); // upsert on (volume_id, frn), not insert
    }

    [Fact]
    public async Task ScanAsync_InterruptedThenResumed_CompletesFully_WithoutRewalkingFinishedChildren()
    {
        const int childCount = 6;
        BuildTree(childDirCount: childCount, filesPerChildDir: 3, rootFileCount: 1);
        long volumeId = InsertVolume();

        var countingWalkScanner = new CountingWalkScanner(new WalkScanner(_directoryEnumerator));
        var orchestrator = MakeOrchestrator(countingWalkScanner);

        // Simulate a crash/kill after 2 of the 6 top-level children finish:
        // cancel as soon as progress reports 2 completions. The cancellation
        // check runs at the START of the next child's loop iteration (see
        // ScanOrchestrator), so exactly 2 children end up checkpointed and
        // no child is ever left half-walked.
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p =>
        {
            if (p.TopLevelChildrenCompleted >= 2)
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => orchestrator.ScanAsync(volumeId, Root(), progress, cts.Token));

        long filesAfterInterruption = CountFiles();
        Assert.True(filesAfterInterruption > 0, "Some work should have landed before the interruption.");
        Assert.True(filesAfterInterruption < 1 + childCount + childCount * 3,
            "The scan should NOT have completed everything before being cancelled.");

        // Resume: same volume, same root, fresh (non-cancelled) token.
        var resumeResult = await orchestrator.ScanAsync(volumeId, Root());

        Assert.Equal(1 + childCount + childCount * 3, CountFiles());
        Assert.True(resumeResult.TopLevelChildrenCompletedThisRun < childCount,
            "The resumed run should have skipped the already-completed children, not redone all of them.");

        // The real payoff: across BOTH runs combined, every child directory
        // was walked exactly once. None were skipped entirely, and none were
        // redundantly re-walked.
        Assert.Equal(childCount, countingWalkScanner.CallCounts.Count);
        Assert.All(countingWalkScanner.CallCounts.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public async Task ScanAsync_ExcludedTopLevelDirectory_IsNeitherWalkedNorCheckpointed()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "node_modules"));
        File.WriteAllText(Path.Combine(_tempRoot, "node_modules", "pkg.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "real"));
        File.WriteAllText(Path.Combine(_tempRoot, "real", "a.txt"), "1");
        long volumeId = InsertVolume();

        var orchestrator = new ScanOrchestrator(
            _directoryEnumerator,
            new WalkScanner(_directoryEnumerator),
            new FileIndexWriter(WriteQueue),
            new ScanCheckpointStore(ConnectionFactory, WriteQueue),
            new DefaultSkipList(windowsRoot: @"C:\Windows", tempRoot: @"C:\zara-test-unused-temp-root"));

        var result = await orchestrator.ScanAsync(volumeId, Root());

        Assert.Equal(1, result.TopLevelChildrenTotal); // only "real" counted — node_modules excluded
        Assert.Equal(2, CountFiles()); // "real" dir + "a.txt"; node_modules never touched
    }

    /// <summary>Never skips anything (see MakeOrchestrator's remarks).</summary>
    private sealed class NoOpSkipList : ISkipList
    {
        public bool ShouldSkip(string canonicalPath, bool isDirectory) => false;
    }

    /// <summary>Reports synchronously on the calling thread — unlike
    /// <see cref="Progress{T}"/>, which posts via a captured
    /// SynchronizationContext and can't be relied on to run before the
    /// caller's next await continues. The cancellation-timing test above
    /// needs the former.</summary>
    private sealed class SyncProgress(Action<ScanProgress> callback) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => callback(value);
    }

    private sealed class CountingWalkScanner(IWalkScanner inner) : IWalkScanner
    {
        public readonly Dictionary<string, int> CallCounts = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<WalkedFile> Walk(CanonicalPath root, WalkOptions? options = null)
        {
            CallCounts[root.Value] = CallCounts.GetValueOrDefault(root.Value) + 1;
            return inner.Walk(root, options);
        }
    }
}
