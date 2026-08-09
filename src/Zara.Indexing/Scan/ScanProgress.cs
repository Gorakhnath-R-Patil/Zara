namespace Zara.Indexing.Scan;

/// <summary>
/// Reported after each top-level child of the scan root finishes (see
/// <c>Migrations/002_scan_checkpoints.sql</c> for why that's the reporting
/// granularity, not "per directory" or "per file").
/// </summary>
public sealed record ScanProgress(
    string RootPath,
    int TopLevelChildrenTotal,
    int TopLevelChildrenCompleted,
    long FilesWritten,
    long FilesSkipped,
    TimeSpan Elapsed);
