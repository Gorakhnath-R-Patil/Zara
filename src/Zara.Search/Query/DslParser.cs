using System.Globalization;

namespace Zara.Search.Query;

/// <summary>
/// Parses the structured query DSL — ARCHITECTURE.md §12.3.
/// </summary>
/// <remarks>
/// <b>Scope, deliberately narrowed from §12.3's full sketch:</b> this parses
/// space-separated predicates, ANDed together implicitly, each optionally
/// negated with a leading <c>NOT</c> token or <c>-</c> prefix. It does NOT
/// implement parenthesized boolean grouping or a general <c>OR</c> between
/// arbitrary predicates (only the field-scoped <c>|</c> in e.g.
/// <c>type:image|video</c>, which is a fixed enumeration, not a general
/// expression). A full recursive-descent boolean expression parser is a
/// meaningfully bigger, separate piece of work — this covers the large
/// majority of what real queries look like (implicit AND of simple
/// predicates) and ships something genuinely useful now rather than nothing
/// until the bigger parser is done. Revisit if usage data shows people
/// actually want <c>(a OR b) AND c</c>-shaped queries.
///
/// <c>content:"..."</c> parses successfully into
/// <see cref="StructuredQuery.ContentContains"/> but nothing executes it
/// yet — full-text search doesn't exist until Phase 2 (§11.6/§12.1). Parsing
/// it now means the DSL's surface is stable and query strings written today
/// don't need to change when content search lands.
/// </remarks>
public static class DslParser
{
    public static StructuredQuery Parse(string query) => Parse(query, DateTimeOffset.UtcNow);

    /// <summary>Testable overload — relative date predicates (<c>modified:&lt;7d</c>)
    /// are resolved against <paramref name="now"/> rather than the live clock.</summary>
    public static StructuredQuery Parse(string query, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tokens = DslLexer.Tokenize(query);
        var builder = new QueryBuilder();

        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            bool negated = false;

            if (string.Equals(token, "NOT", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= tokens.Count)
                {
                    break; // trailing "NOT" with nothing after it — ignore
                }

                negated = true;
                token = tokens[++i];
            }
            else if (token.Length > 1 && token[0] == '-')
            {
                // "-ext:tmp" negates. A lone "-" (length 1) falls through to
                // the bare-word path below and is treated as a literal token.
                negated = true;
                token = token[1..];
            }

            int colon = token.IndexOf(':');
            if (colon <= 0)
            {
                // No recognized field prefix — a bare word is an implicit
                // name-substring predicate.
                (negated ? builder.ExcludedNameTerms : builder.NameTerms).Add(token);
                continue;
            }

            string field = token[..colon].ToLowerInvariant();
            string value = token[(colon + 1)..];

            switch (field)
            {
                case "ext":
                    AddCsv(negated ? builder.ExcludedExtensions : builder.Extensions, value, lower: true);
                    break;

                case "name":
                    (negated ? builder.ExcludedNameTerms : builder.NameTerms).Add(StripQuotes(value));
                    break;

                case "size":
                    builder.Size = ParseSizeRange(value);
                    break;

                case "modified":
                    builder.Modified = ParseDateRange(value, now);
                    break;

                case "created":
                    builder.Created = ParseDateRange(value, now);
                    break;

                case "accessed":
                    builder.Accessed = ParseDateRange(value, now);
                    break;

                case "path":
                    builder.PathContains = StripQuotes(value);
                    break;

                case "in":
                    builder.InScope = StripQuotes(value);
                    break;

                case "depth":
                    if (int.TryParse(value, out int depth))
                    {
                        builder.Depth = depth;
                    }
                    break;

                case "type":
                    builder.TypeClasses.AddRange(value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(v => v.ToLowerInvariant()));
                    break;

                case "attr":
                    AddCsv(builder.Attributes, value, lower: true);
                    break;

                case "dup":
                    builder.IsDuplicate = ParseBool(value);
                    break;

                case "empty":
                    builder.IsEmpty = ParseBool(value);
                    break;

                case "content":
                    builder.ContentContains = StripQuotes(value);
                    break;

                case "sort":
                    ApplySort(builder, value);
                    if (i + 1 < tokens.Count && TryParseSortDirection(tokens[i + 1], out var dir))
                    {
                        builder.SortDirection = dir;
                        i++;
                    }
                    break;

                case "limit":
                    if (int.TryParse(value, out int limit) && limit > 0)
                    {
                        builder.Limit = limit;
                    }
                    break;

                default:
                    // Unrecognized field prefix (e.g. a colon inside a
                    // filename someone typed literally): fall back to
                    // treating the WHOLE original token as a name term
                    // rather than silently dropping it.
                    (negated ? builder.ExcludedNameTerms : builder.NameTerms).Add(token);
                    break;
            }
        }

        return builder.Build();
    }

    private static void ApplySort(QueryBuilder builder, string value) => builder.Sort = value.ToLowerInvariant() switch
    {
        "size" => SortField.Size,
        "modified" or "date" => SortField.Modified,
        "name" => SortField.Name,
        _ => SortField.Relevance,
    };

    private static bool TryParseSortDirection(string token, out SortDirection direction)
    {
        switch (token.ToLowerInvariant())
        {
            case "asc":
                direction = SortDirection.Ascending;
                return true;
            case "desc":
                direction = SortDirection.Descending;
                return true;
            default:
                direction = default;
                return false;
        }
    }

    private static void AddCsv(List<string> target, string value, bool lower)
    {
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            target.Add(lower ? part.ToLowerInvariant() : part);
        }
    }

    private static string StripQuotes(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static bool? ParseBool(string value) => value.ToLowerInvariant() switch
    {
        "true" or "1" or "yes" => true,
        "false" or "0" or "no" => false,
        _ => null,
    };

    // ── size:>100mb  size:<10kb  size:100kb..2mb  size:500 ──────────────────
    private static readonly (string Suffix, double Multiplier)[] SizeSuffixes =
    [
        ("tb", 1024.0 * 1024 * 1024 * 1024),
        ("gb", 1024.0 * 1024 * 1024),
        ("mb", 1024.0 * 1024),
        ("kb", 1024.0),
        ("b", 1.0),
    ];

    internal static SizeRange? ParseSizeRange(string value)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value[0] == '>')
        {
            long? min = ParseByteCount(value[1..]);
            return min is null ? null : new SizeRange(min, null);
        }

        if (value[0] == '<')
        {
            long? max = ParseByteCount(value[1..]);
            return max is null ? null : new SizeRange(null, max);
        }

        int rangeSep = value.IndexOf("..", StringComparison.Ordinal);
        if (rangeSep >= 0)
        {
            string lowPart = value[..rangeSep];
            string highPart = value[(rangeSep + 2)..];
            long? min = lowPart.Length > 0 ? ParseByteCount(lowPart) : null;
            long? max = highPart.Length > 0 ? ParseByteCount(highPart) : null;
            return min is null && max is null ? null : new SizeRange(min, max);
        }

        // A bare value with no operator is treated as a minimum — the
        // common intent ("size:100mb" reads as "at least 100MB") is closer
        // to a floor than an exact byte match, which is rarely what anyone
        // means for a file size predicate.
        long? bareMin = ParseByteCount(value);
        return bareMin is null ? null : new SizeRange(bareMin, null);
    }

    private static long? ParseByteCount(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        double multiplier = 1;
        string numberPart = text;

        foreach (var (suffix, mult) in SizeSuffixes)
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                multiplier = mult;
                numberPart = text[..^suffix.Length];
                break;
            }
        }

        return double.TryParse(numberPart.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
            ? (long)(n * multiplier)
            : null;
    }

    // ── modified:<7d  modified:>30d  modified:2024-01..2024-06  modified:2024-06-01 ─
    internal static DateRange? ParseDateRange(string value, DateTimeOffset now)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value[0] == '<')
        {
            return TryParseRelativeDays(value[1..], out int days) ? new DateRange(now.AddDays(-days), null) : null;
        }

        if (value[0] == '>')
        {
            return TryParseRelativeDays(value[1..], out int days) ? new DateRange(null, now.AddDays(-days)) : null;
        }

        int rangeSep = value.IndexOf("..", StringComparison.Ordinal);
        if (rangeSep >= 0)
        {
            string lowPart = value[..rangeSep];
            string highPart = value[(rangeSep + 2)..];
            DateTimeOffset? after = lowPart.Length > 0 && TryParseDate(lowPart, out var d1) ? d1 : null;
            DateTimeOffset? before = highPart.Length > 0 && TryParseDate(highPart, out var d2) ? d2.AddDays(1) : null;
            return after is null && before is null ? null : new DateRange(after, before);
        }

        return TryParseDate(value, out var single) ? new DateRange(single, single.AddDays(1)) : null;
    }

    private static bool TryParseRelativeDays(string text, out int days)
    {
        text = text.Trim();
        if (text.Length > 0 && (text[^1] is 'd' or 'D'))
        {
            text = text[..^1];
        }

        return int.TryParse(text, out days) && days >= 0;
    }

    private static bool TryParseDate(string text, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result);

    /// <summary>Mutable accumulator — kept private and internal to parsing;
    /// <see cref="StructuredQuery"/> itself stays an immutable record.</summary>
    private sealed class QueryBuilder
    {
        public List<string> NameTerms { get; } = [];
        public List<string> ExcludedNameTerms { get; } = [];
        public List<string> Extensions { get; } = [];
        public List<string> ExcludedExtensions { get; } = [];
        public SizeRange? Size { get; set; }
        public DateRange? Modified { get; set; }
        public DateRange? Created { get; set; }
        public DateRange? Accessed { get; set; }
        public string? PathContains { get; set; }
        public string? InScope { get; set; }
        public int? Depth { get; set; }
        public List<string> TypeClasses { get; } = [];
        public List<string> Attributes { get; } = [];
        public bool? IsDuplicate { get; set; }
        public bool? IsEmpty { get; set; }
        public string? ContentContains { get; set; }
        public SortField Sort { get; set; } = SortField.Relevance;
        public SortDirection SortDirection { get; set; } = SortDirection.Descending;
        public int Limit { get; set; } = 200;

        public StructuredQuery Build() => new()
        {
            NameTerms = NameTerms,
            ExcludedNameTerms = ExcludedNameTerms,
            Extensions = Extensions,
            ExcludedExtensions = ExcludedExtensions,
            Size = Size,
            Modified = Modified,
            Created = Created,
            Accessed = Accessed,
            PathContains = PathContains,
            InScope = InScope,
            Depth = Depth,
            TypeClasses = TypeClasses,
            Attributes = Attributes,
            IsDuplicate = IsDuplicate,
            IsEmpty = IsEmpty,
            ContentContains = ContentContains,
            Sort = Sort,
            SortDirection = SortDirection,
            Limit = Limit,
        };
    }
}
