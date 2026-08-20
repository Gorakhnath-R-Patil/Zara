namespace Zara.Ai.QueryCompilation;

/// <summary>
/// The deterministic-first router — ARCHITECTURE.md §14.2: "most queries
/// never see a model". Tries structured-DSL detection, then a single bare
/// word, then a table of known phrasings, all before conceding the query
/// needs the LLM compiler. §14.2's target is measured, not assumed: T46's
/// golden-set harness reports the real bypass rate against this router.
/// </summary>
public interface IIntentRouter
{
    /// <param name="now">Defaults to the live clock; overridable for
    /// deterministic tests of relative-date patterns ("files from today").</param>
    RoutedQuery Route(string rawQuery, DateTimeOffset? now = null);
}
