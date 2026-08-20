using System.Globalization;

namespace Zara.Ai.QueryCompilation;

public sealed record ValidationResult(bool IsValid, string? ErrorMessage)
{
    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string message) => new(false, message);
}

/// <summary>
/// T44 — post-generation validation, the second of the two layers §14.3
/// relies on to make a small model's output trustworthy: the JSON Schema
/// (enforced by the model provider's structured-output sampler) guarantees
/// well-formed shape; this guarantees the VALUES inside that shape make
/// sense (schema → semantic → clamping, exactly the order named in
/// ARCHITECTURE.md §14.3's "Post-generation validation" line). Grammar
/// constraints stop a model from emitting a field that doesn't exist; they
/// don't stop it from emitting <c>min_bytes: 999999999999</c> or a
/// self-contradictory date range, which is what this layer catches.
/// </summary>
public static class QueryOutputValidator
{
    private static readonly HashSet<string> ValidIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        "find_files", "find_content", "analyze_storage", "find_duplicates",
        "summarize", "compare", "propose_operation", "clarify", "out_of_scope",
    };

    private static readonly HashSet<string> ValidFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "document", "image", "video", "audio", "code", "archive", "spreadsheet", "presentation", "pdf",
    };

    private static readonly HashSet<string> ValidPathScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop", "documents", "downloads", "pictures", "videos", "music",
        "onedrive", "projects", "current_folder", "everywhere",
    };

    private static readonly HashSet<string> ValidSorts = new(StringComparer.OrdinalIgnoreCase)
    {
        "relevance", "size_desc", "modified_desc", "modified_asc", "name_asc",
    };

    public static ValidationResult Validate(LlmQueryOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (string.IsNullOrWhiteSpace(output.Intent) || !ValidIntents.Contains(output.Intent))
        {
            return ValidationResult.Fail($"Unknown or missing intent '{output.Intent}'.");
        }

        foreach (string fileType in output.FileTypes ?? [])
        {
            if (!ValidFileTypes.Contains(fileType))
            {
                return ValidationResult.Fail($"Unknown file_type '{fileType}'.");
            }
        }

        foreach (string scope in output.PathScope ?? [])
        {
            if (!ValidPathScopes.Contains(scope))
            {
                return ValidationResult.Fail($"Unknown path_scope '{scope}'.");
            }
        }

        if (output.Sort is not null && !ValidSorts.Contains(output.Sort))
        {
            return ValidationResult.Fail($"Unknown sort '{output.Sort}'.");
        }

        if (output.MinBytes is < 0 || output.MaxBytes is < 0)
        {
            return ValidationResult.Fail("min_bytes/max_bytes must not be negative.");
        }

        if (output.MinBytes is { } min && output.MaxBytes is { } max && min > max)
        {
            return ValidationResult.Fail($"min_bytes ({min}) is greater than max_bytes ({max}).");
        }

        DateTimeOffset? modifiedAfter = null;
        if (output.ModifiedAfter is { Length: > 0 } afterText)
        {
            if (!TryParseDate(afterText, out modifiedAfter))
            {
                return ValidationResult.Fail($"modified_after '{afterText}' is not a valid date.");
            }
        }

        DateTimeOffset? modifiedBefore = null;
        if (output.ModifiedBefore is { Length: > 0 } beforeText)
        {
            if (!TryParseDate(beforeText, out modifiedBefore))
            {
                return ValidationResult.Fail($"modified_before '{beforeText}' is not a valid date.");
            }
        }

        if (modifiedAfter is { } a && modifiedBefore is { } b && a > b)
        {
            return ValidationResult.Fail("modified_after is later than modified_before.");
        }

        // A future date is never a meaningful bound for "when was this file
        // last modified" — most likely a model hallucination or a
        // relative-date miscalculation, not a real user intent to filter on.
        var now = DateTimeOffset.UtcNow;
        if (modifiedAfter > now || modifiedBefore > now)
        {
            return ValidationResult.Fail("modified_after/modified_before must not be in the future.");
        }

        if (output.Confidence is < 0 or > 1)
        {
            return ValidationResult.Fail($"confidence {output.Confidence} is outside [0, 1].");
        }

        return ValidationResult.Ok();
    }

    /// <summary>Clamps values that are individually valid but out of the
    /// range downstream code expects — applied only after
    /// <see cref="Validate"/> has already rejected genuinely nonsensical
    /// output; this is for "technically fine but should be bounded", not
    /// "wrong".</summary>
    public static void Clamp(LlmQueryOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.Limit = Math.Clamp(output.Limit ?? 200, 1, 500);

        if (output.Keywords is { Count: > 12 } keywords)
        {
            output.Keywords = keywords[..12];
        }

        if (output.Extensions is { Count: > 20 } extensions)
        {
            output.Extensions = extensions[..20];
        }

        if (output.SemanticQuery is { Length: > 300 } semanticQuery)
        {
            output.SemanticQuery = semanticQuery[..300];
        }

        if (output.ClarifyQuestion is { Length: > 160 } clarifyQuestion)
        {
            output.ClarifyQuestion = clarifyQuestion[..160];
        }
    }

    private static bool TryParseDate(string text, out DateTimeOffset? result)
    {
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }
}
