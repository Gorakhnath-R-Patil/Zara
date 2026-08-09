using Microsoft.Data.Sqlite;

namespace Zara.Search.Analytics;

/// <summary>
/// Maintains <c>folder_stats</c> (§22): per-directory file counts and total
/// size, recursive over the whole subtree. Deterministic (§32: "Folder size
/// rollup... deterministic"), and depends on <c>files.parent_id</c> being
/// populated — see <c>Zara.Indexing.ParentIdBackfiller</c>, which this type
/// does not run itself (a caller runs the backfill, then this).
/// </summary>
public interface ISizeRollup
{
    /// <summary>Recomputes <c>folder_stats</c> for every live directory in
    /// the volume from scratch. Not incremental (ARCHITECTURE.md §22
    /// describes a <c>dirty</c>-flag incremental-propagation design for
    /// production scale; this full recomputation is the simpler, correct
    /// baseline — see the class remarks on <c>SizeRollup</c> for the
    /// tradeoff). Returns the number of directories updated.</summary>
    Task<int> RecomputeAsync(SqliteConnection connection, long volumeId, CancellationToken cancellationToken = default);
}
