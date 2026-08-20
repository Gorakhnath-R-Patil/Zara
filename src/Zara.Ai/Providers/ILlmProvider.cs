namespace Zara.Ai.Providers;

/// <param name="SystemPrompt">Static across calls where possible — Ollama's prefix
/// cache reuses the KV cache for an identical prefix, which is what turns a ~1.7s
/// compile into ~0.6s (ARCHITECTURE.md §14.4). Keep dynamic content OUT of this
/// field and in <see cref="UserPrompt"/> instead.</param>
/// <param name="JsonSchema">Raw JSON Schema text. When set, the model's output is
/// grammar-constrained to match it (Ollama's structured-output `format` field) —
/// this is the mechanism §14.3 depends on to make a 4B model reliable: it cannot
/// emit invalid JSON or hallucinate a field name, because the sampler makes those
/// tokens unreachable. Null means free-form text (used for summarization, not
/// query compilation).</param>
/// <param name="KeepAlive">How long Ollama keeps the model resident after this
/// call. Defaults to 30 minutes (§7.1) — reloading a 2.5GB model from disk costs
/// 3-6s and would otherwise dominate every "cold" query's latency.</param>
public sealed record LlmRequest(
    string SystemPrompt,
    string UserPrompt,
    string? JsonSchema = null,
    double Temperature = 0.1,
    int? Seed = null,
    int NumPredict = 512,
    int NumCtx = 8192,
    TimeSpan? KeepAlive = null);

public sealed record LlmResponse(bool Success, string Content, string? ErrorMessage = null, TimeSpan? Duration = null);

/// <summary>
/// A local or cloud LLM backend — ARCHITECTURE.md §14.1's provider
/// abstraction. Deliberately narrower than the architecture doc's full
/// sketch (no streaming, no capability negotiation): §14.3's query
/// compiler is the only M8 consumer, and it needs exactly one JSON object
/// back per call, not a token stream. Extend when a second consumer
/// (summarization, the agent loop) actually needs more.
/// </summary>
public interface ILlmProvider
{
    string ModelId { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);

    /// <summary>A cheap liveness probe — distinct from a real completion so
    /// callers (and tests) can tell "the model is unreachable" apart from
    /// "the model returned something we didn't expect" before paying for a
    /// full generation.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
