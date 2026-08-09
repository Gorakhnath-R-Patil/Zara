namespace Zara.Search.Query;

public interface ISelectivityEstimator
{
    /// <summary>Returns a value in [0, 1]: near 1.0 means the query is
    /// expected to match a large fraction of the index (unselective); near
    /// 0.0 means it's expected to narrow sharply.</summary>
    double Estimate(StructuredQuery query);
}

/// <summary>
/// A static, hand-tuned heuristic — not a statistics-based estimator (no
/// query against real column cardinalities, no histogram). ARCHITECTURE.md
/// §8.3 describes a <c>SelectivityEstimator</c> that "estimates selectivity,
/// picks execution order, sets budgets"; a real implementation of that needs
/// either live table statistics or SQLite's own query planner stats, neither
/// of which exist yet at M3. This heuristic is cheap, deterministic, and
/// directionally correct (more predicates ⇒ lower/more-selective score) —
/// good enough to rank candidate plans against each other now. Replace with
/// a statistics-backed estimator once <c>ANALYZE</c>/table stats are wired
/// up, if this heuristic's ranking ever proves wrong in practice.
/// </summary>
public sealed class SelectivityEstimator : ISelectivityEstimator
{
    public double Estimate(StructuredQuery query)
    {
        double selectivity = 1.0;

        if (query.NameTerms.Count > 0)
        {
            selectivity *= 0.05; // a name substring is usually the sharpest filter available
        }

        if (query.Extensions.Count > 0)
        {
            selectivity *= 0.15;
        }

        if (query.TypeClasses.Count > 0)
        {
            selectivity *= 0.30;
        }

        if (query.Size is not null)
        {
            selectivity *= 0.50;
        }

        if (query.Modified is not null)
        {
            selectivity *= 0.40;
        }

        if (query.Created is not null)
        {
            selectivity *= 0.40;
        }

        if (query.Accessed is not null)
        {
            selectivity *= 0.40;
        }

        if (query.Attributes.Count > 0)
        {
            selectivity *= 0.60;
        }

        if (query.Depth is not null)
        {
            selectivity *= 0.30;
        }

        return Math.Clamp(selectivity, 0.0, 1.0);
    }
}
