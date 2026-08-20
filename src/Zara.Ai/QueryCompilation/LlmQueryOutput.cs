namespace Zara.Ai.QueryCompilation;

/// <summary>
/// The shape the LLM's JSON-schema-constrained output deserializes into —
/// ARCHITECTURE.md §14.3's abridged schema. Deliberately mutable/nullable
/// throughout (unlike <c>StructuredQuery</c>): this is a landing zone for
/// UNTRUSTED, not-yet-validated model output, not a domain type — see
/// <c>QueryOutputValidator</c>, which is what stands between this and
/// anything downstream ever seeing it.
/// </summary>
public sealed class LlmQueryOutput
{
    public string? Intent { get; set; }
    public string? SemanticQuery { get; set; }
    public List<string>? Keywords { get; set; }
    public List<string>? Extensions { get; set; }
    public List<string>? FileTypes { get; set; }
    public List<string>? PathScope { get; set; }
    public long? MinBytes { get; set; }
    public long? MaxBytes { get; set; }
    public string? ModifiedAfter { get; set; }
    public string? ModifiedBefore { get; set; }
    public string? Sort { get; set; }
    public int? Limit { get; set; }
    public string? ClarifyQuestion { get; set; }
    public double Confidence { get; set; }
}
