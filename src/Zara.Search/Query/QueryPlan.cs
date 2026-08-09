namespace Zara.Search.Query;

/// <summary>
/// An executable plan against the <c>files</c> table: a parameterized SQL
/// fragment, not a full statement — callers splice
/// <see cref="WhereClause"/>/<see cref="OrderByClause"/> into their own
/// <c>SELECT ... FROM files WHERE {WhereClause} {OrderByClause} LIMIT {Limit}</c>
/// (or a <c>COUNT(*)</c> variant) rather than this type owning the whole query,
/// so the same plan works whether the caller wants rows or just a count.
/// </summary>
/// <param name="UnsupportedPredicates">Predicates the input
/// <see cref="StructuredQuery"/> specified that this planner could NOT
/// translate into SQL against the current schema — e.g. <c>path:</c>/<c>in:</c>
/// need full-path storage the <c>files</c> table doesn't have yet (§22's
/// design defers that to <c>parent_id</c> chain reconstruction, not yet
/// populated — see TRACKER.md's M2 decision log). Surfaced explicitly rather
/// than silently dropped, so a caller (and a test) can tell the difference
/// between "this query matched nothing" and "part of this query was ignored".</param>
public sealed record QueryPlan(
    string WhereClause,
    IReadOnlyDictionary<string, object> Parameters,
    string OrderByClause,
    int Limit,
    double EstimatedSelectivity,
    IReadOnlyList<string> UnsupportedPredicates);
