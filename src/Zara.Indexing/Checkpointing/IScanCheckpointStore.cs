namespace Zara.Indexing.Checkpointing;

/// <summary>
/// Tracks which top-level children of a scan root have been fully walked —
/// the resumability mechanism behind T15/T16. See
/// <c>Migrations/002_scan_checkpoints.sql</c> for why the granularity is
/// "top-level child", not "individual directory".
/// </summary>
public interface IScanCheckpointStore
{
    Task<IReadOnlySet<string>> GetCompletedChildrenAsync(long volumeId, string rootPath, CancellationToken cancellationToken = default);

    Task MarkChildCompletedAsync(long volumeId, string rootPath, string childName, CancellationToken cancellationToken = default);

    /// <summary>Clears all checkpoint rows for this root — called once a scan
    /// finishes fully, so a future re-scan starts clean rather than treating
    /// last time's children as still "already done".</summary>
    Task ClearAsync(long volumeId, string rootPath, CancellationToken cancellationToken = default);
}
