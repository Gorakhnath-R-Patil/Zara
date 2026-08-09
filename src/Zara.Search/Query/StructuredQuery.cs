namespace Zara.Search.Query;

public enum SortField { Relevance, Size, Modified, Name }
public enum SortDirection { Ascending, Descending }

public sealed record SizeRange(long? MinBytes, long? MaxBytes);
public sealed record DateRange(DateTimeOffset? After, DateTimeOffset? Before);

/// <summary>
/// The compiled form of the structured query DSL (ARCHITECTURE.md §12.3) —
/// the language both the manual command bar AND the LLM query compiler
/// (§14.3, a later milestone) target, so the AI path and the manual path
/// share one execution engine (§12.3's stated architectural payoff).
/// </summary>
public sealed record StructuredQuery
{
    public IReadOnlyList<string> NameTerms { get; init; } = [];
    public IReadOnlyList<string> ExcludedNameTerms { get; init; } = [];

    public IReadOnlyList<string> Extensions { get; init; } = [];
    public IReadOnlyList<string> ExcludedExtensions { get; init; } = [];

    public SizeRange? Size { get; init; }
    public DateRange? Modified { get; init; }
    public DateRange? Created { get; init; }
    public DateRange? Accessed { get; init; }

    /// <summary>Path must contain this substring anywhere.</summary>
    public string? PathContains { get; init; }

    /// <summary>Search is scoped to this directory and its descendants only.</summary>
    public string? InScope { get; init; }

    public int? Depth { get; init; }

    public IReadOnlyList<string> TypeClasses { get; init; } = [];
    public IReadOnlyList<string> Attributes { get; init; } = [];

    public bool? IsDuplicate { get; init; }
    public bool? IsEmpty { get; init; }

    /// <summary>Parsed, but not executable until Phase 2's FTS integration
    /// exists — see DslParser's remarks.</summary>
    public string? ContentContains { get; init; }

    public SortField Sort { get; init; } = SortField.Relevance;
    public SortDirection SortDirection { get; init; } = SortDirection.Descending;
    public int Limit { get; init; } = 200;
}
