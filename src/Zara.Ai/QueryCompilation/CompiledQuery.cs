using Zara.Search.Query;

namespace Zara.Ai.QueryCompilation;

/// <param name="Query">Set only when <see cref="Success"/> is true.</param>
/// <param name="Confidence">The model's own confidence, [0, 1] — surfaced even
/// on success so a caller can decide whether to show results directly or hedge.</param>
/// <param name="ClarifyQuestion">Set when the model judged the request too
/// ambiguous to guess at (low confidence or the model asked one directly) —
/// ARCHITECTURE.md §13's "confidence-gated clarification": below 0.5, ask
/// instead of guessing.</param>
/// <param name="ErrorMessage">Set on outright failure — an unreachable LLM,
/// invalid JSON, or output that failed <see cref="QueryOutputValidator"/>.</param>
public sealed record CompiledQuery(bool Success, StructuredQuery? Query, double Confidence, string? ClarifyQuestion, string? ErrorMessage)
{
    public static CompiledQuery Failed(string errorMessage) => new(false, null, 0, null, errorMessage);

    public static CompiledQuery NeedsClarification(double confidence, string? question) =>
        new(false, null, confidence, question, null);

    public static CompiledQuery Ok(StructuredQuery query, double confidence) =>
        new(true, query, confidence, null, null);
}
