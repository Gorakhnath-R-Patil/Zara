using System.Text.RegularExpressions;
using Zara.Search.Query;

namespace Zara.Ai.QueryCompilation;

/// <inheritdoc cref="IIntentRouter"/>
/// <remarks>
/// The pattern table below is a genuine, individually-tested set — not
/// padded to hit ARCHITECTURE.md §14.2's illustrative "~40 patterns" figure.
/// It covers the phrasings that map cleanly onto a
/// <see cref="StructuredQuery"/> today; intents that need analytics
/// components this router doesn't have access to yet (e.g. "largest
/// folders", which needs <c>SizeRollup</c>'s folder-level aggregates rather
/// than a file-level filter) are deliberately not faked here. Add real
/// patterns as real phrasings prove common — that's what T46's golden set
/// is for measuring.
/// </remarks>
public sealed class IntentRouter : IIntentRouter
{
    private static readonly HashSet<string> KnownDslFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "ext", "name", "size", "modified", "created", "accessed", "path",
        "in", "depth", "type", "attr", "dup", "empty", "content", "sort", "limit",
    };

    private readonly record struct IntentPattern(string Name, Regex Pattern, Func<Match, DateTimeOffset, StructuredQuery> Build);

    private static readonly IntentPattern[] Patterns =
    [
        new("glob-extension", Re(@"^\*\.(?<ext>[a-z0-9]{1,10})$"),
            (m, _) => new StructuredQuery { Extensions = [m.Groups["ext"].Value.ToLowerInvariant()] }),

        new("screenshots", Re(@"^screenshots?$"),
            (_, _) => new StructuredQuery { NameTerms = ["screenshot"], TypeClasses = ["image"] }),

        new("recent-downloads", Re(@"^(recent|latest)\s+downloads?$"),
            (_, _) => new StructuredQuery { InScope = "Downloads", Sort = SortField.Modified, SortDirection = SortDirection.Descending }),

        new("files-from-today", Re(@"^files?\s+from\s+today$"),
            (_, now) => new StructuredQuery { Modified = new DateRange(StartOfDay(now), null) }),

        new("files-from-this-week", Re(@"^files?\s+from\s+this\s+week$"),
            (_, now) => new StructuredQuery { Modified = new DateRange(now.AddDays(-7), null) }),

        new("files-from-this-month", Re(@"^files?\s+from\s+this\s+month$"),
            (_, now) => new StructuredQuery { Modified = new DateRange(now.AddDays(-30), null) }),

        new("empty-folders", Re(@"^empty\s+folders?$"),
            (_, _) => new StructuredQuery { IsEmpty = true }),

        new("duplicates", Re(@"^duplicate(s|d)?( files?)?$"),
            (_, _) => new StructuredQuery { IsDuplicate = true }),

        new("large-files", Re(@"^(large|big)\s+files?$"),
            (_, _) => new StructuredQuery { Size = new SizeRange(100L * 1024 * 1024, null), Sort = SortField.Size, SortDirection = SortDirection.Descending }),

        new("huge-files", Re(@"^(huge|massive|enormous)\s+files?$"),
            (_, _) => new StructuredQuery { Size = new SizeRange(1024L * 1024 * 1024, null), Sort = SortField.Size, SortDirection = SortDirection.Descending }),

        new("old-files", Re(@"^old(er)?\s+files?$"),
            (_, now) => new StructuredQuery { Modified = new DateRange(null, now.AddDays(-180)), Sort = SortField.Modified, SortDirection = SortDirection.Ascending }),

        new("hidden-files", Re(@"^hidden\s+files?$"),
            (_, _) => new StructuredQuery { Attributes = ["hidden"] }),

        new("readonly-files", Re(@"^read.?only\s+files?$"),
            (_, _) => new StructuredQuery { Attributes = ["readonly"] }),

        new("type-image", Re(@"^(images?|photos?|pictures?)$"),
            (_, _) => new StructuredQuery { TypeClasses = ["image"] }),

        new("type-video", Re(@"^(videos?|movies?|clips?)$"),
            (_, _) => new StructuredQuery { TypeClasses = ["video"] }),

        new("type-audio", Re(@"^(music|audio( files?)?|songs?)$"),
            (_, _) => new StructuredQuery { TypeClasses = ["audio"] }),

        new("type-document", Re(@"^documents?$"),
            (_, _) => new StructuredQuery { TypeClasses = ["document"] }),

        new("type-spreadsheet", Re(@"^spreadsheets?$"),
            (_, _) => new StructuredQuery { TypeClasses = ["spreadsheet"] }),

        new("type-presentation", Re(@"^(presentations?|slides?)$"),
            (_, _) => new StructuredQuery { TypeClasses = ["presentation"] }),

        new("type-archive", Re(@"^(archives?|zip( files?)?|compressed( files?)?)$"),
            (_, _) => new StructuredQuery { TypeClasses = ["archive"] }),

        new("type-code", Re(@"^(code|source)( files?)?$"),
            (_, _) => new StructuredQuery { TypeClasses = ["code"] }),
    ];

    public RoutedQuery Route(string rawQuery, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(rawQuery);
        DateTimeOffset effectiveNow = now ?? DateTimeOffset.UtcNow;
        string trimmed = NormalizeWhitespace(rawQuery);

        if (trimmed.Length == 0)
        {
            return new RoutedQuery(RouteDecision.RequiresLlm, rawQuery, null);
        }

        if (LooksLikeStructuredDsl(trimmed))
        {
            return new RoutedQuery(RouteDecision.Structured, rawQuery, DslParser.Parse(trimmed, effectiveNow));
        }

        // Checked BEFORE the single-token fallback, not after: a common
        // single word like "screenshots" or "*.pdf" is far more useful
        // mapped to a rich intent (type:image; ext:pdf) than treated as a
        // literal name-substring search for the word "screenshots" — which
        // would rarely match anything. ARCHITECTURE.md §14.2's router
        // sketch lists "single token" before "matches a known intent" but
        // doesn't specifically address single-WORD intents; this ordering
        // is the more useful reading of it, confirmed by this class's own
        // test suite once the two were tried both ways.
        foreach (var pattern in Patterns)
        {
            var match = pattern.Pattern.Match(trimmed);
            if (match.Success)
            {
                return new RoutedQuery(RouteDecision.PatternMatched, rawQuery, pattern.Build(match, effectiveNow), pattern.Name);
            }
        }

        if (!trimmed.Contains(' '))
        {
            return new RoutedQuery(RouteDecision.SingleToken, rawQuery, DslParser.Parse(trimmed, effectiveNow));
        }

        return new RoutedQuery(RouteDecision.RequiresLlm, rawQuery, null);
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset value) => new(value.Year, value.Month, value.Day, 0, 0, 0, value.Offset);

    /// <summary>A query counts as "already structured DSL" if any of its
    /// whitespace-separated tokens is a recognized <c>field:value</c> pair —
    /// the same field set <see cref="DslParser"/> understands. A single such
    /// token is enough: "resume ext:pdf" is DSL-flavored even though "resume"
    /// alone would also parse as a bare name term.</summary>
    private static bool LooksLikeStructuredDsl(string query)
    {
        foreach (string rawToken in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string token = rawToken.Length > 1 && rawToken[0] == '-' ? rawToken[1..] : rawToken;
            int colon = token.IndexOf(':');
            if (colon > 0 && KnownDslFields.Contains(token[..colon]))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeWhitespace(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

    private static Regex Re(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
