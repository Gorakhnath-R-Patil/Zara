using Zara.Search.Query;

namespace Zara.Ai.QueryCompilation;

/// <param name="Query">Null when <see cref="Decision"/> is <see cref="RouteDecision.RequiresLlm"/> —
/// there is nothing to execute yet; the caller hands <see cref="RawQuery"/> to the query compiler (T43).</param>
public sealed record RoutedQuery(RouteDecision Decision, string RawQuery, StructuredQuery? Query, string? MatchedPattern = null);
