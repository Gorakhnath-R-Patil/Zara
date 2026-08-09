using Zara.Core.Files;

namespace Zara.Search.Names;

/// <summary>
/// In-memory filename/substring index — ARCHITECTURE.md §12.2, the structure
/// that makes N-03 (name search p95 &lt;20ms @ 2M files) achievable. Neither
/// FTS5 nor a plain <c>LIKE '%x%'</c> does fast unanchored substring matching
/// well; this is purpose-built for exactly that.
/// </summary>
public interface INameIndex
{
    /// <summary>Adds or updates an entry. Safe to call incrementally while a
    /// scan is still in progress — search results reflect whatever has been
    /// upserted so far (ARCHITECTURE.md §11.3: "search works during first scan").</summary>
    void Upsert(FileId fileId, string name);

    /// <summary>Removes an entry. A no-op if the id isn't present.</summary>
    void Remove(FileId fileId);

    /// <summary>Ranked matches for <paramref name="query"/>, best first.
    /// Multi-word queries are ANDed — every token must match.</summary>
    IReadOnlyList<NameHit> Search(string query, int maxResults = 50);

    int Count { get; }
}
