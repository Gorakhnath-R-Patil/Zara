using System.Text.Json;
using Zara.Ai.Providers;
using Zara.Search.Query;

namespace Zara.Ai.QueryCompilation;

/// <inheritdoc cref="IQueryCompiler"/>
/// <remarks>
/// The system prompt is a fixed, static string reused byte-for-byte on
/// every call — ARCHITECTURE.md §14.4: a static prefix lets Ollama reuse
/// the KV cache across calls, which is what turns a ~1.7s cold compile into
/// ~0.6s warm. Dynamic content (the user's actual query) goes entirely in
/// <c>UserPrompt</c>, never interpolated into the system prompt, specifically
/// to keep that prefix identical across calls.
/// </remarks>
public sealed class QueryCompiler : IQueryCompiler
{
    private const string SystemPrompt = """
        You translate a user's natural-language file search request into a
        structured query object matching the given JSON schema.

        Rules:
        - Only use fields defined by the schema. Never invent a field name.
        - Never output a raw file path. path_scope must be one of the
          enumerated location names, not free text.
        - If the request is ambiguous or you are not confident, set
          confidence below 0.5 and provide a short clarify_question instead
          of guessing at filters.
        - keywords are individual search terms extracted from the request,
          not the whole sentence restated.
        - Dates in modified_after/modified_before must be ISO 8601
          (YYYY-MM-DD), computed relative to the request's intent, never a
          literal date the user did not imply.
        """;

    private const string Schema = """
        {
          "type": "object",
          "required": ["intent", "confidence"],
          "additionalProperties": false,
          "properties": {
            "intent": {
              "type": "string",
              "enum": ["find_files", "find_content", "analyze_storage", "find_duplicates", "summarize", "compare", "propose_operation", "clarify", "out_of_scope"]
            },
            "semantic_query": { "type": "string", "maxLength": 300 },
            "keywords": { "type": "array", "items": { "type": "string" }, "maxItems": 12 },
            "extensions": { "type": "array", "items": { "type": "string" } },
            "file_types": {
              "type": "array",
              "items": { "type": "string", "enum": ["document", "image", "video", "audio", "code", "archive", "spreadsheet", "presentation", "pdf"] }
            },
            "path_scope": {
              "type": "array",
              "items": { "type": "string", "enum": ["desktop", "documents", "downloads", "pictures", "videos", "music", "onedrive", "projects", "current_folder", "everywhere"] }
            },
            "min_bytes": { "type": "integer", "minimum": 0 },
            "max_bytes": { "type": "integer", "minimum": 0 },
            "modified_after": { "type": "string" },
            "modified_before": { "type": "string" },
            "sort": { "type": "string", "enum": ["relevance", "size_desc", "modified_desc", "modified_asc", "name_asc"] },
            "limit": { "type": "integer", "minimum": 1, "maximum": 500 },
            "clarify_question": { "type": "string", "maxLength": 160 },
            "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
          }
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILlmProvider _llm;

    public QueryCompiler(ILlmProvider llm)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    }

    public async Task<CompiledQuery> CompileAsync(string naturalLanguageQuery, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalLanguageQuery);

        var response = await _llm.CompleteAsync(new LlmRequest(
            SystemPrompt: SystemPrompt,
            UserPrompt: naturalLanguageQuery,
            JsonSchema: Schema,
            Temperature: 0.1,
            NumPredict: 300), cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
            return CompiledQuery.Failed(response.ErrorMessage ?? "The model call failed with no error message.");
        }

        LlmQueryOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<LlmQueryOutput>(response.Content, JsonOptions);
        }
        catch (JsonException ex)
        {
            return CompiledQuery.Failed($"Model output was not valid JSON: {ex.Message}");
        }

        if (output is null)
        {
            return CompiledQuery.Failed("Model returned an empty response.");
        }

        var validation = QueryOutputValidator.Validate(output);
        if (!validation.IsValid)
        {
            return CompiledQuery.Failed(validation.ErrorMessage!);
        }

        QueryOutputValidator.Clamp(output);

        // Confidence-gated clarification (§13): below 0.5, or the model
        // asked outright — either way, don't guess at filters.
        if (output.Confidence < 0.5 || !string.IsNullOrWhiteSpace(output.ClarifyQuestion))
        {
            return CompiledQuery.NeedsClarification(output.Confidence, output.ClarifyQuestion);
        }

        return CompiledQuery.Ok(ToStructuredQuery(output), output.Confidence);
    }

    private static StructuredQuery ToStructuredQuery(LlmQueryOutput output)
    {
        var extensions = new List<string>(output.Extensions ?? []);
        var typeClasses = new List<string>();

        foreach (string fileType in output.FileTypes ?? [])
        {
            // "pdf" is its own file_type in the LLM-facing vocabulary (it's
            // common enough to call out explicitly) but QueryPlanner's
            // type-class table doesn't have a "pdf" entry — it belongs in
            // Extensions, alongside anything else the model named directly.
            if (string.Equals(fileType, "pdf", StringComparison.OrdinalIgnoreCase))
            {
                extensions.Add("pdf");
            }
            else
            {
                typeClasses.Add(fileType.ToLowerInvariant());
            }
        }

        SizeRange? size = output.MinBytes is null && output.MaxBytes is null
            ? null
            : new SizeRange(output.MinBytes, output.MaxBytes);

        DateRange? modified = output.ModifiedAfter is null && output.ModifiedBefore is null
            ? null
            : new DateRange(
                ParseDateOrNull(output.ModifiedAfter),
                ParseDateOrNull(output.ModifiedBefore));

        var (sortField, sortDirection) = MapSort(output.Sort);

        return new StructuredQuery
        {
            NameTerms = output.Keywords ?? [],
            Extensions = extensions,
            TypeClasses = typeClasses,
            InScope = MapPathScope(output.PathScope),
            Size = size,
            Modified = modified,
            Sort = sortField,
            SortDirection = sortDirection,
            Limit = output.Limit ?? 200,
        };
    }

    private static DateTimeOffset? ParseDateOrNull(string? text) =>
        text is { Length: > 0 } && DateTimeOffset.TryParse(text, out var value) ? value : null;

    /// <summary>Only the first named location becomes <c>InScope</c> —
    /// <c>StructuredQuery</c> has one scope, not a set; a model naming
    /// several is an edge case not worth a multi-scope query shape yet.
    /// "everywhere"/"current_folder"/"projects" all mean "no folder-name
    /// scope restriction" at the <c>StructuredQuery</c> level today (the
    /// last two have no fixed real-folder mapping) — same "parsed but not
    /// yet fully honored" posture as <c>QueryPlanner</c>'s
    /// <c>UnsupportedPredicates</c> for <c>in:</c> (T20).</summary>
    private static string? MapPathScope(List<string>? scopes)
    {
        if (scopes is not { Count: > 0 })
        {
            return null;
        }

        return scopes[0].ToLowerInvariant() switch
        {
            "desktop" => "Desktop",
            "documents" => "Documents",
            "downloads" => "Downloads",
            "pictures" => "Pictures",
            "videos" => "Videos",
            "music" => "Music",
            "onedrive" => "OneDrive",
            _ => null,
        };
    }

    private static (SortField Field, SortDirection Direction) MapSort(string? sort) => sort?.ToLowerInvariant() switch
    {
        "size_desc" => (SortField.Size, SortDirection.Descending),
        "modified_desc" => (SortField.Modified, SortDirection.Descending),
        "modified_asc" => (SortField.Modified, SortDirection.Ascending),
        "name_asc" => (SortField.Name, SortDirection.Ascending),
        _ => (SortField.Relevance, SortDirection.Descending),
    };
}
