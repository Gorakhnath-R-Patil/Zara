namespace Zara.Indexing.Scan;

public sealed record ScanResult(
    long FilesWritten,
    long FilesSkipped,
    int TopLevelChildrenTotal,
    int TopLevelChildrenCompletedThisRun,
    TimeSpan Elapsed);
