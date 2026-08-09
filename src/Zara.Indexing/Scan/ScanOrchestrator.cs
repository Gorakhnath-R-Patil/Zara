using System.Diagnostics;
using Zara.Core.Files;
using Zara.Filesystem.Enumeration;
using Zara.Indexing.Checkpointing;
using Zara.Indexing.Writing;
using Zara.Volumes.Fallback;
using Zara.Volumes.Skip;

namespace Zara.Indexing.Scan;

/// <inheritdoc cref="IScanOrchestrator"/>
public sealed class ScanOrchestrator : IScanOrchestrator
{
    // ARCHITECTURE.md §11.3: "Batched writer: 5,000 rows per transaction".
    private const int BatchSize = 5000;

    private readonly IDirectoryEnumerator _directoryEnumerator;
    private readonly IWalkScanner _walkScanner;
    private readonly IFileIndexWriter _writer;
    private readonly IScanCheckpointStore _checkpoints;
    private readonly ISkipList _skipList;

    public ScanOrchestrator(
        IDirectoryEnumerator directoryEnumerator,
        IWalkScanner walkScanner,
        IFileIndexWriter writer,
        IScanCheckpointStore checkpoints,
        ISkipList? skipList = null)
    {
        _directoryEnumerator = directoryEnumerator ?? throw new ArgumentNullException(nameof(directoryEnumerator));
        _walkScanner = walkScanner ?? throw new ArgumentNullException(nameof(walkScanner));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
        _skipList = skipList ?? new DefaultSkipList();
    }

    public async Task<ScanResult> ScanAsync(
        long volumeId, CanonicalPath root, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        long written = 0;
        long skipped = 0;

        var alreadyCompleted = await _checkpoints
            .GetCompletedChildrenAsync(volumeId, root.Value, cancellationToken)
            .ConfigureAwait(false);

        var topLevel = _directoryEnumerator.Enumerate(root).ToList();

        // Files directly under the root are cheap and never checkpointed
        // individually — just write them all up front, once.
        var rootFiles = new List<WalkedFile>();
        foreach (var entry in topLevel.Where(e => !e.IsDirectory))
        {
            string path = CombinePath(root, entry.Name);
            if (!_skipList.ShouldSkip(path, isDirectory: false))
            {
                rootFiles.Add(ToWalkedFile(path, entry, depth: 1));
            }
        }

        if (rootFiles.Count > 0)
        {
            var result = await _writer.UpsertBatchAsync(volumeId, rootFiles, cancellationToken).ConfigureAwait(false);
            written += result.Written;
            skipped += result.SkippedNoFrn;
        }

        var childDirectories = topLevel
            .Where(e => e.IsDirectory && !_skipList.ShouldSkip(CombinePath(root, e.Name), isDirectory: true))
            .ToList();

        int completedCount = alreadyCompleted.Count;
        int completedThisRun = 0;

        foreach (var childEntry in childDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (alreadyCompleted.Contains(childEntry.Name))
            {
                continue; // the whole point of checkpointing — see 002_scan_checkpoints.sql
            }

            string childPathValue = CombinePath(root, childEntry.Name);
            var childPath = CanonicalPath.FromCanonicalizedString(childPathValue);
            var walkOptions = new WalkOptions { SkipList = _skipList };

            var batch = new List<WalkedFile>(BatchSize);

            foreach (var walked in _walkScanner.Walk(childPath, walkOptions))
            {
                // WalkScanner counts depth relative to whatever root it's
                // given; childPath is itself one level below the scan root,
                // so every depth it reports needs +1 to be correct relative
                // to `root`. (This also means the 64-level MaxDepth cap is
                // evaluated one level looser than nominal from the true
                // root's perspective — a documented, harmless slack, not a
                // correctness bug.)
                batch.Add(walked with { Depth = walked.Depth + 1 });

                if (batch.Count >= BatchSize)
                {
                    var result = await _writer.UpsertBatchAsync(volumeId, batch, cancellationToken).ConfigureAwait(false);
                    written += result.Written;
                    skipped += result.SkippedNoFrn;
                    batch.Clear();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            // The child directory's own row (WalkScanner only yields its
            // DESCENDANTS, not childPath itself).
            batch.Add(ToWalkedFile(childPathValue, childEntry, depth: 1));

            var finalResult = await _writer.UpsertBatchAsync(volumeId, batch, cancellationToken).ConfigureAwait(false);
            written += finalResult.Written;
            skipped += finalResult.SkippedNoFrn;

            await _checkpoints.MarkChildCompletedAsync(volumeId, root.Value, childEntry.Name, cancellationToken).ConfigureAwait(false);
            completedCount++;
            completedThisRun++;

            progress?.Report(new ScanProgress(root.Value, childDirectories.Count, completedCount, written, skipped, stopwatch.Elapsed));
        }

        if (completedCount >= childDirectories.Count)
        {
            await _checkpoints.ClearAsync(volumeId, root.Value, cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        return new ScanResult(written, skipped, childDirectories.Count, completedThisRun, stopwatch.Elapsed);
    }

    private static string CombinePath(CanonicalPath root, string name) => root.Value.TrimEnd('\\') + "\\" + name;

    private static WalkedFile ToWalkedFile(string path, RawDirectoryEntry entry, int depth) => new(
        CanonicalPath.FromCanonicalizedString(path),
        entry.Name,
        entry.Frn,
        entry.IsDirectory,
        entry.SizeBytes,
        entry.CreatedUtc,
        entry.ModifiedUtc,
        entry.AccessedUtc,
        entry.Attributes,
        depth);
}
