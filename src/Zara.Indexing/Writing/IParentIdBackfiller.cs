namespace Zara.Indexing.Writing;

/// <summary>
/// Resolves every populatable <c>files.parent_id</c> for a volume in one
/// pass, using each row's <c>parent_path_hash</c> (set at write time by
/// <see cref="FileIndexWriter"/>) matched against other rows' <c>path_hash</c>.
/// See <c>003_parent_path_hash.sql</c> for why this two-step (hash now,
/// resolve later) approach exists instead of populating <c>parent_id</c>
/// directly during the scan.
/// </summary>
public interface IParentIdBackfiller
{
    /// <summary>Idempotent — safe to run after every scan, or periodically.
    /// Returns the number of rows the backfill touched (including rows
    /// re-set to the same value, and rows correctly reset to NULL because
    /// their apparent parent no longer matches any row).</summary>
    Task<int> RunAsync(long volumeId, CancellationToken cancellationToken = default);
}
