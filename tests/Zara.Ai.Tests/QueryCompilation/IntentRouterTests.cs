using Zara.Ai.QueryCompilation;
using Zara.Search.Query;

namespace Zara.Ai.Tests.QueryCompilation;

public class IntentRouterTests
{
    private readonly IntentRouter _sut = new();
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    // ── Structured DSL short-circuit ────────────────────────────────────────

    [Theory]
    [InlineData("ext:pdf")]
    [InlineData("resume ext:pdf")]
    [InlineData("size:>100mb")]
    [InlineData("NOT ext:tmp")]
    [InlineData("path:Downloads modified:<7d")]
    public void Route_StructuredDslSyntax_IsRoutedWithoutLlm(string query)
    {
        var result = _sut.Route(query, Now);

        Assert.Equal(RouteDecision.Structured, result.Decision);
        Assert.NotNull(result.Query);
    }

    [Fact]
    public void Route_StructuredDsl_ProducesTheSameResultAsCallingDslParserDirectly()
    {
        // Field-by-field, not Assert.Equal(direct, routed.Query): StructuredQuery's
        // list-typed properties are List<T>, which records compare by
        // reference (List<T> has no value equality), not by content — so
        // two independently-parsed instances are never "record-equal" even
        // with identical contents. Not a production bug, just not what
        // whole-record Assert.Equal can check here.
        var routed = _sut.Route("ext:pdf,docx size:>1mb", Now);
        var direct = DslParser.Parse("ext:pdf,docx size:>1mb", Now);

        Assert.Equal(direct.Extensions, routed.Query!.Extensions);
        Assert.Equal(direct.Size, routed.Query.Size);
    }

    // ── Single bare token ────────────────────────────────────────────────────

    [Theory]
    [InlineData("resume")]
    [InlineData("budget.xlsx")]
    public void Route_SingleToken_IsRoutedWithoutLlm(string query)
    {
        var result = _sut.Route(query, Now);

        Assert.Equal(RouteDecision.SingleToken, result.Decision);
        Assert.Equal([query], result.Query!.NameTerms);
    }

    // ── Pattern-matched intents ──────────────────────────────────────────────

    [Fact]
    public void Route_GlobExtension_MapsToExtensionFilter()
    {
        var result = _sut.Route("*.pdf", Now);

        Assert.Equal(RouteDecision.PatternMatched, result.Decision);
        Assert.Equal("glob-extension", result.MatchedPattern);
        Assert.Equal(["pdf"], result.Query!.Extensions);
    }

    [Theory]
    [InlineData("screenshot")]
    [InlineData("screenshots")]
    public void Route_Screenshots_MapsToNameAndTypeFilter(string query)
    {
        var result = _sut.Route(query, Now);

        Assert.Equal("screenshots", result.MatchedPattern);
        Assert.Equal(["screenshot"], result.Query!.NameTerms);
        Assert.Equal(["image"], result.Query.TypeClasses);
    }

    [Theory]
    [InlineData("recent downloads")]
    [InlineData("latest downloads")]
    [InlineData("Recent Downloads")]
    public void Route_RecentDownloads_MapsToScopeAndSort(string query)
    {
        var result = _sut.Route(query, Now);

        Assert.Equal("recent-downloads", result.MatchedPattern);
        Assert.Equal("Downloads", result.Query!.InScope);
        Assert.Equal(SortField.Modified, result.Query.Sort);
        Assert.Equal(SortDirection.Descending, result.Query.SortDirection);
    }

    [Fact]
    public void Route_FilesFromToday_UsesStartOfDayAsTheLowerBound()
    {
        var result = _sut.Route("files from today", Now);

        Assert.Equal("files-from-today", result.MatchedPattern);
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero), result.Query!.Modified!.After);
    }

    [Fact]
    public void Route_EmptyFolders_SetsIsEmpty()
    {
        var result = _sut.Route("empty folders", Now);

        Assert.True(result.Query!.IsEmpty);
    }

    [Theory]
    [InlineData("duplicates")]
    [InlineData("duplicate files")]
    [InlineData("duplicated files")]
    public void Route_Duplicates_SetsIsDuplicate(string query)
    {
        var result = _sut.Route(query, Now);

        Assert.True(result.Query!.IsDuplicate);
    }

    [Fact]
    public void Route_LargeFiles_SetsA100MbFloorAndSortsBySize()
    {
        var result = _sut.Route("large files", Now);

        Assert.Equal(100L * 1024 * 1024, result.Query!.Size!.MinBytes);
        Assert.Equal(SortField.Size, result.Query.Sort);
    }

    [Fact]
    public void Route_HugeFiles_SetsA1GbFloor()
    {
        var result = _sut.Route("huge files", Now);

        Assert.Equal(1024L * 1024 * 1024, result.Query!.Size!.MinBytes);
    }

    [Fact]
    public void Route_OldFiles_SetsA180DayUpperBound_SortedOldestFirst()
    {
        var result = _sut.Route("old files", Now);

        Assert.Equal(Now.AddDays(-180), result.Query!.Modified!.Before);
        Assert.Equal(SortDirection.Ascending, result.Query.SortDirection);
    }

    [Fact]
    public void Route_HiddenFiles_SetsHiddenAttribute()
    {
        var result = _sut.Route("hidden files", Now);

        Assert.Equal(["hidden"], result.Query!.Attributes);
    }

    [Theory]
    [InlineData("read-only files")]
    [InlineData("readonly files")]
    public void Route_ReadonlyFiles_SetsReadonlyAttribute(string query)
    {
        var result = _sut.Route(query, Now);

        Assert.Equal(["readonly"], result.Query!.Attributes);
    }

    [Theory]
    [InlineData("images", "image")]
    [InlineData("photos", "image")]
    [InlineData("pictures", "image")]
    [InlineData("videos", "video")]
    [InlineData("movies", "video")]
    [InlineData("music", "audio")]
    [InlineData("songs", "audio")]
    [InlineData("documents", "document")]
    [InlineData("spreadsheets", "spreadsheet")]
    [InlineData("presentations", "presentation")]
    [InlineData("slides", "presentation")]
    [InlineData("archives", "archive")]
    [InlineData("zip files", "archive")]
    [InlineData("code", "code")]
    [InlineData("source files", "code")]
    public void Route_TypeClassPhrasings_MapToTheExpectedTypeClass(string query, string expectedType)
    {
        var result = _sut.Route(query, Now);

        Assert.Equal(RouteDecision.PatternMatched, result.Decision);
        Assert.Equal([expectedType], result.Query!.TypeClasses);
    }

    // ── Falls through to the LLM ─────────────────────────────────────────────

    [Theory]
    [InlineData("find my resume from the japan trip")]
    [InlineData("what did I write about the kafka migration")]
    [InlineData("show me files related to the budget meeting")]
    public void Route_NaturalLanguageWithNoMatchingPattern_RequiresLlm(string query)
    {
        var result = _sut.Route(query, Now);

        Assert.Equal(RouteDecision.RequiresLlm, result.Decision);
        Assert.Null(result.Query);
    }

    [Fact]
    public void Route_EmptyString_RequiresLlm()
    {
        var result = _sut.Route("", Now);

        Assert.Equal(RouteDecision.RequiresLlm, result.Decision);
    }

    [Fact]
    public void Route_WhitespaceOnly_RequiresLlm()
    {
        var result = _sut.Route("   ", Now);

        Assert.Equal(RouteDecision.RequiresLlm, result.Decision);
    }

    // ── Precedence: DSL/single-token checks run before the pattern table ────

    [Fact]
    public void Route_PatternLikeTextWithADslToken_IsStructuredNotPatternMatched()
    {
        // Contains "large files" phrasing AND a DSL token — DSL wins.
        var result = _sut.Route("large files ext:pdf", Now);

        Assert.Equal(RouteDecision.Structured, result.Decision);
        Assert.Equal(["pdf"], result.Query!.Extensions);
    }
}
