namespace Zara.Search.Names;

/// <summary>
/// How a result matched the query, best to worst. Declared in ranking order
/// on purpose — <see cref="NameIndex"/> sorts ascending by this enum's
/// numeric value, so inserting a new kind means choosing carefully where it
/// belongs in this list, not just appending it.
/// </summary>
public enum NameMatchKind
{
    /// <summary>The folded name equals the folded query exactly.</summary>
    Exact = 0,

    /// <summary>The folded name starts with the folded query.</summary>
    Prefix = 1,

    /// <summary>The query appears right after a separator (space, dash,
    /// underscore, dot, slash) — e.g. "resume" matching "2024-resume.pdf".</summary>
    WordBoundary = 2,

    /// <summary>Every query token is present somewhere in the name, but not
    /// as a word-boundary or prefix match (or, for multi-word queries, not
    /// even as one contiguous run).</summary>
    Substring = 3,
}
