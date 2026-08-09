namespace Zara.Search.Query;

/// <inheritdoc cref="IQueryPlanner"/>
public sealed class QueryPlanner : IQueryPlanner
{
    // Extension → type-class mapping, since files.type_class isn't populated
    // yet (MIME/type detection is Tier-2 content work, not shipped —
    // ARCHITECTURE.md §11.2). Translating type: into an extension IN-list
    // client-side is a small, honest way to make it actually work now
    // instead of marking it unsupported.
    private static readonly Dictionary<string, string[]> TypeClassExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image"] = ["jpg", "jpeg", "png", "gif", "bmp", "webp", "svg", "tiff", "heic"],
        ["video"] = ["mp4", "mkv", "avi", "mov", "wmv", "webm", "flv"],
        ["audio"] = ["mp3", "wav", "flac", "aac", "ogg", "m4a"],
        ["document"] = ["pdf", "doc", "docx", "odt", "rtf", "txt", "md"],
        ["spreadsheet"] = ["xls", "xlsx", "csv", "ods"],
        ["presentation"] = ["ppt", "pptx", "odp"],
        ["archive"] = ["zip", "rar", "7z", "tar", "gz"],
        ["code"] = ["cs", "java", "py", "js", "ts", "go", "rs", "cpp", "c", "h", "sql", "json", "xml", "html", "css"],
    };

    // files.attributes is a FILE_ATTRIBUTE_* bitmask (System.IO.FileAttributes
    // values, which are numerically identical) — attr: predicates become
    // bitwise-AND conditions, which SQLite's "&" operator supports directly.
    private static readonly Dictionary<string, int> AttributeBits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["readonly"] = (int)FileAttributes.ReadOnly,
        ["hidden"] = (int)FileAttributes.Hidden,
        ["system"] = (int)FileAttributes.System,
        ["directory"] = (int)FileAttributes.Directory,
        ["archive"] = (int)FileAttributes.Archive,
        ["reparsepoint"] = (int)FileAttributes.ReparsePoint,
        ["compressed"] = (int)FileAttributes.Compressed,
        ["encrypted"] = (int)FileAttributes.Encrypted,
    };

    private readonly ISelectivityEstimator _selectivityEstimator;

    public QueryPlanner(ISelectivityEstimator? selectivityEstimator = null)
    {
        _selectivityEstimator = selectivityEstimator ?? new SelectivityEstimator();
    }

    public QueryPlan Plan(StructuredQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var conditions = new List<string> { "deleted_utc IS NULL" };
        var parameters = new Dictionary<string, object>();
        var unsupported = new List<string>();
        int paramCounter = 0;

        AddNameTerms(query, conditions, parameters);
        AddExtensions(query, conditions, parameters, ref paramCounter);
        AddSize(query, conditions, parameters);
        AddDateRange("modified_utc", query.Modified, conditions, parameters);
        AddDateRange("created_utc", query.Created, conditions, parameters);
        AddDateRange("accessed_utc", query.Accessed, conditions, parameters);
        AddTypeClasses(query, conditions, parameters, ref paramCounter, unsupported);
        AddAttributes(query, conditions, unsupported);
        AddDepth(query, conditions, parameters);

        if (query.PathContains is not null)
        {
            unsupported.Add("path"); // no full-path column yet — see QueryPlan's remarks
        }

        if (query.InScope is not null)
        {
            unsupported.Add("in");
        }

        if (query.IsDuplicate is not null)
        {
            unsupported.Add("dup"); // needs quick_hash/content_hash grouping — M4's DuplicateFinder
        }

        if (query.IsEmpty is not null)
        {
            unsupported.Add("empty"); // needs folder_stats child counts — M4's SizeRollup
        }

        if (query.ContentContains is not null)
        {
            unsupported.Add("content"); // needs FTS5 — Phase 2
        }

        string whereClause = string.Join(" AND ", conditions);
        string orderByClause = BuildOrderBy(query);
        double selectivity = _selectivityEstimator.Estimate(query);

        return new QueryPlan(whereClause, parameters, orderByClause, query.Limit, selectivity, unsupported);
    }

    private static void AddNameTerms(StructuredQuery query, List<string> conditions, Dictionary<string, object> parameters)
    {
        // The wildcards/escaping are baked into the bound parameter VALUE,
        // never into the SQL text itself — a term containing "%", "_", or a
        // literal "'" is still just data to SQLite, not syntax. That's the
        // actual injection boundary in this file; see AddInList for the
        // other place values become parameters instead of interpolated text.
        for (int i = 0; i < query.NameTerms.Count; i++)
        {
            string name = $"@nameTerm{i}";
            conditions.Add($"name_folded LIKE {name} ESCAPE '\\'");
            parameters[name] = ToLikePattern(query.NameTerms[i]);
        }

        for (int i = 0; i < query.ExcludedNameTerms.Count; i++)
        {
            string name = $"@excludedNameTerm{i}";
            conditions.Add($"name_folded NOT LIKE {name} ESCAPE '\\'");
            parameters[name] = ToLikePattern(query.ExcludedNameTerms[i]);
        }
    }

    /// <summary>Wraps a term as a <c>%...%</c> LIKE pattern, escaping the
    /// characters LIKE treats specially (<c>%</c>, <c>_</c>, and the escape
    /// character itself) so a term that happens to contain them is matched
    /// literally, not as a wildcard.</summary>
    private static string ToLikePattern(string term)
    {
        string escaped = term
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static void AddExtensions(
        StructuredQuery query, List<string> conditions, Dictionary<string, object> parameters, ref int paramCounter)
    {
        if (query.Extensions.Count > 0)
        {
            string inList = AddInList(query.Extensions, parameters, ref paramCounter);
            conditions.Add($"ext IN ({inList})");
        }

        if (query.ExcludedExtensions.Count > 0)
        {
            string inList = AddInList(query.ExcludedExtensions, parameters, ref paramCounter);
            conditions.Add($"(ext IS NULL OR ext NOT IN ({inList}))");
        }
    }

    private static void AddSize(StructuredQuery query, List<string> conditions, Dictionary<string, object> parameters)
    {
        if (query.Size?.MinBytes is { } min)
        {
            conditions.Add("size_bytes >= @sizeMin");
            parameters["@sizeMin"] = min;
        }

        if (query.Size?.MaxBytes is { } max)
        {
            conditions.Add("size_bytes <= @sizeMax");
            parameters["@sizeMax"] = max;
        }
    }

    private static void AddDateRange(
        string column, DateRange? range, List<string> conditions, Dictionary<string, object> parameters)
    {
        if (range is null)
        {
            return;
        }

        if (range.After is { } after)
        {
            string p = $"@{column}After";
            conditions.Add($"{column} >= {p}");
            parameters[p] = after.ToUnixTimeMilliseconds();
        }

        if (range.Before is { } before)
        {
            string p = $"@{column}Before";
            conditions.Add($"{column} < {p}");
            parameters[p] = before.ToUnixTimeMilliseconds();
        }
    }

    private static void AddTypeClasses(
        StructuredQuery query, List<string> conditions, Dictionary<string, object> parameters,
        ref int paramCounter, List<string> unsupported)
    {
        if (query.TypeClasses.Count == 0)
        {
            return;
        }

        var extensions = new List<string>();
        foreach (string typeClass in query.TypeClasses)
        {
            if (TypeClassExtensions.TryGetValue(typeClass, out var exts))
            {
                extensions.AddRange(exts);
            }
            else
            {
                unsupported.Add($"type:{typeClass}");
            }
        }

        if (extensions.Count > 0)
        {
            string inList = AddInList(extensions.Distinct(), parameters, ref paramCounter);
            conditions.Add($"ext IN ({inList})");
        }
    }

    private static void AddAttributes(StructuredQuery query, List<string> conditions, List<string> unsupported)
    {
        foreach (string attr in query.Attributes)
        {
            if (AttributeBits.TryGetValue(attr, out int bit))
            {
                conditions.Add($"(attributes & {bit}) != 0");
            }
            else
            {
                unsupported.Add($"attr:{attr}");
            }
        }
    }

    private static void AddDepth(StructuredQuery query, List<string> conditions, Dictionary<string, object> parameters)
    {
        if (query.Depth is { } depth)
        {
            conditions.Add("depth = @depth");
            parameters["@depth"] = depth;
        }
    }

    private static string AddInList(IEnumerable<string> values, Dictionary<string, object> parameters, ref int paramCounter)
    {
        var placeholders = new List<string>();

        foreach (string value in values)
        {
            string name = $"@p{paramCounter++}";
            parameters[name] = value;
            placeholders.Add(name);
        }

        return string.Join(", ", placeholders);
    }

    private static string BuildOrderBy(StructuredQuery query)
    {
        string direction = query.SortDirection == SortDirection.Ascending ? "ASC" : "DESC";

        return query.Sort switch
        {
            SortField.Size => $"ORDER BY size_bytes {direction}",
            SortField.Modified => $"ORDER BY modified_utc {direction}",
            SortField.Name => $"ORDER BY name_folded {direction}",

            // "Relevance" (no explicit sort:) has no real meaning yet — no
            // FTS/vector scoring exists before Phase 2. The original choice
            // here was to default to `ORDER BY modified_utc DESC` as "the
            // least-arbitrary default"; T21's 500k-scale benchmark showed
            // that choice costing ~85ms p95 (vs. a 25ms target), because
            // modified_utc isn't covered by whatever index served the
            // filter predicates (e.g. ix_files_ext_size), so SQLite has to
            // filesort the filtered set before applying LIMIT. No ORDER BY
            // at all — natural rowid order, effectively free — is the
            // honest default until a real relevance signal exists to sort
            // by. A caller that wants recency ordering can ask for it
            // explicitly via `sort:modified`, as a deliberate cost tradeoff
            // rather than a hidden one.
            _ => string.Empty,
        };
    }
}
