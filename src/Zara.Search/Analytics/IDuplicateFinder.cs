using Microsoft.Data.Sqlite;

namespace Zara.Search.Analytics;

/// <summary>
/// Deterministic duplicate detection — ARCHITECTURE.md §11.3/§32.1's B14:
/// quick-hash pre-filter, then a content-hash confirmation pass. No AI
/// involved (§32, "Duplicate detection → deterministic").
/// </summary>
/// <remarks>
/// Unlike <c>IQueryPlanner</c> (which stays storage-agnostic and only
/// produces a SQL fragment for the caller to run), this type runs its own
/// query and returns structured results — duplicate grouping is a
/// GROUP BY/aggregate operation, not a filter fragment composable into a
/// larger query, so there's no equivalent portability benefit to preserve.
/// That's why <c>Zara.Search</c> takes a light <c>Microsoft.Data.Sqlite</c>
/// dependency here specifically, while <c>Query/</c> does not.
///
/// <b>Path resolution is intentionally NOT this type's job:</b> confirming a
/// candidate group needs to read real file content, which needs real paths —
/// and <c>Zara.Search</c> has neither (paths aren't stored per-row yet,
/// §22/M2's decision log, and reading files is <c>Zara.Filesystem</c>'s
/// concern). <see cref="Confirm"/> takes a caller-supplied hash function so
/// this stays decoupled from both.
/// </remarks>
public interface IDuplicateFinder
{
    /// <summary>Groups of 2+ files sharing both size and quick_hash — see
    /// <see cref="Confirm"/>'s remarks on why these are candidates, not
    /// confirmed duplicates.</summary>
    Task<IReadOnlyList<DuplicateGroup>> FindCandidatesAsync(SqliteConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Splits a candidate group into confirmed duplicate sub-groups by
    /// content hash. A quick-hash collision between files that are NOT
    /// actually identical is possible (though rare) — e.g. two files whose
    /// first/last 4KB and size happen to match but differ in the middle
    /// (exactly the case <c>QuickHasherTests</c> demonstrates deliberately).
    /// <paramref name="computeContentHash"/> is typically backed by
    /// <c>IContentHasher</c> plus a file-id-to-path lookup the caller
    /// already has.
    /// </summary>
    IReadOnlyList<DuplicateGroup> Confirm(DuplicateGroup candidate, Func<long, string> computeContentHash);
}
