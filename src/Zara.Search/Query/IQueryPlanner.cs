namespace Zara.Search.Query;

/// <summary>Translates a <see cref="StructuredQuery"/> into a SQL plan against
/// the <c>files</c> table — the "metadata-only path" (no FTS5, no
/// <c>sqlite-vec</c>) that's all that exists before Phase 2 lands content
/// extraction and embeddings.</summary>
public interface IQueryPlanner
{
    QueryPlan Plan(StructuredQuery query);
}
