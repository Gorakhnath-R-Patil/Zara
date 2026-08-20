namespace Zara.Ai.QueryCompilation;

public enum RouteDecision
{
    /// <summary>The raw text already parses as the structured DSL (§12.3) — executed directly, no LLM.</summary>
    Structured,

    /// <summary>A single bare word — treated as a name search, no LLM.</summary>
    SingleToken,

    /// <summary>Matched one of <see cref="IntentRouter"/>'s known phrasings — templated into a
    /// <see cref="Zara.Search.Query.StructuredQuery"/> directly, no LLM.</summary>
    PatternMatched,

    /// <summary>None of the above — needs the LLM query compiler (§14.3, T43).</summary>
    RequiresLlm,
}
