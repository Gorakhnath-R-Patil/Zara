using Zara.Search.Query;

namespace Zara.Search.Tests.Query;

public class DslParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    private static StructuredQuery Parse(string query) => DslParser.Parse(query, Now);

    // ── Bare words → name terms ──────────────────────────────────────────────

    [Fact]
    public void Parse_BareWord_BecomesNameTerm()
    {
        var q = Parse("resume");

        Assert.Equal(["resume"], q.NameTerms);
    }

    [Fact]
    public void Parse_MultipleBareWords_AllBecomeNameTerms()
    {
        var q = Parse("japan resume");

        Assert.Equal(["japan", "resume"], q.NameTerms);
    }

    [Fact]
    public void Parse_UnrecognizedFieldPrefix_FallsBackToWholeTokenAsNameTerm()
    {
        var q = Parse("weird:thing");

        Assert.Equal(["weird:thing"], q.NameTerms);
    }

    // ── ext: ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Ext_SingleValue()
    {
        var q = Parse("ext:pdf");
        Assert.Equal(["pdf"], q.Extensions);
    }

    [Fact]
    public void Parse_Ext_CommaSeparated_LowercasesAndSplits()
    {
        var q = Parse("ext:PDF,DOCX");
        Assert.Equal(["pdf", "docx"], q.Extensions);
    }

    // ── size: ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("size:>100mb", 100L * 1024 * 1024, null)]
    [InlineData("size:<10kb", null, 10L * 1024)]
    [InlineData("size:1gb", 1024L * 1024 * 1024, null)] // bare value -> minimum
    [InlineData("size:500", 500L, null)]                 // no suffix -> bytes
    public void Parse_Size_SingleBound(string query, long? expectedMin, long? expectedMax)
    {
        var q = Parse(query);

        Assert.NotNull(q.Size);
        Assert.Equal(expectedMin, q.Size!.MinBytes);
        Assert.Equal(expectedMax, q.Size.MaxBytes);
    }

    [Fact]
    public void Parse_Size_Range()
    {
        var q = Parse("size:10kb..2mb");

        Assert.Equal(10L * 1024, q.Size!.MinBytes);
        Assert.Equal(2L * 1024 * 1024, q.Size.MaxBytes);
    }

    [Fact]
    public void Parse_Size_FractionalValue()
    {
        var q = Parse("size:>1.5mb");

        Assert.Equal((long)(1.5 * 1024 * 1024), q.Size!.MinBytes);
    }

    // ── modified: / created: / accessed: ────────────────────────────────────

    [Fact]
    public void Parse_Modified_RelativeLessThan_MeansAfterNMinusDays()
    {
        var q = Parse("modified:<7d");

        Assert.Equal(Now.AddDays(-7), q.Modified!.After);
        Assert.Null(q.Modified.Before);
    }

    [Fact]
    public void Parse_Modified_RelativeGreaterThan_MeansOlderThanNDays()
    {
        var q = Parse("modified:>180d");

        Assert.Equal(Now.AddDays(-180), q.Modified!.Before);
        Assert.Null(q.Modified.After);
    }

    [Fact]
    public void Parse_Modified_AbsoluteRange()
    {
        var q = Parse("modified:2024-01-01..2024-06-01");

        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), q.Modified!.After);
        // Upper bound is exclusive-next-day of the range end.
        Assert.Equal(new DateTimeOffset(2024, 6, 2, 0, 0, 0, TimeSpan.Zero), q.Modified.Before);
    }

    [Fact]
    public void Parse_Created_And_Accessed_UseTheSameGrammar()
    {
        var q = Parse("created:<1d accessed:>365d");

        Assert.Equal(Now.AddDays(-1), q.Created!.After);
        Assert.Equal(Now.AddDays(-365), q.Accessed!.Before);
    }

    // ── path: / in: / depth: ─────────────────────────────────────────────────

    [Fact]
    public void Parse_Path_IsSubstringFilter()
    {
        var q = Parse("path:Downloads");
        Assert.Equal("Downloads", q.PathContains);
    }

    [Fact]
    public void Parse_In_IsScope()
    {
        var q = Parse(@"in:C:\Projects");
        Assert.Equal(@"C:\Projects", q.InScope);
    }

    [Fact]
    public void Parse_Depth_ParsesAsInt()
    {
        var q = Parse("depth:1");
        Assert.Equal(1, q.Depth);
    }

    // ── type: / attr: ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Type_PipeSeparated()
    {
        var q = Parse("type:image|video");
        Assert.Equal(["image", "video"], q.TypeClasses);
    }

    [Fact]
    public void Parse_Attr_CommaSeparated()
    {
        var q = Parse("attr:hidden,readonly");
        Assert.Equal(["hidden", "readonly"], q.Attributes);
    }

    // ── dup: / empty: ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("dup:true", true)]
    [InlineData("dup:false", false)]
    public void Parse_Dup_ParsesBoolean(string query, bool expected)
    {
        Assert.Equal(expected, Parse(query).IsDuplicate);
    }

    [Fact]
    public void Parse_Empty_ParsesBoolean()
    {
        Assert.True(Parse("empty:true").IsEmpty);
    }

    // ── content: ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Content_StripsQuotes()
    {
        var q = Parse("content:\"kafka consumer\"");
        Assert.Equal("kafka consumer", q.ContentContains);
    }

    // ── NOT / negation ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NotPrefix_NegatesTheFollowingPredicate()
    {
        var q = Parse("NOT ext:tmp");

        Assert.Empty(q.Extensions);
        Assert.Equal(["tmp"], q.ExcludedExtensions);
    }

    [Fact]
    public void Parse_DashPrefix_AlsoNegates()
    {
        var q = Parse("-ext:tmp");

        Assert.Equal(["tmp"], q.ExcludedExtensions);
    }

    [Fact]
    public void Parse_NotPrefix_OnABareWord_ExcludesIt()
    {
        var q = Parse("NOT screenshot");

        Assert.Equal(["screenshot"], q.ExcludedNameTerms);
        Assert.Empty(q.NameTerms);
    }

    [Fact]
    public void Parse_TrailingNot_WithNothingAfter_IsIgnored()
    {
        var exception = Record.Exception(() => Parse("resume NOT"));
        Assert.Null(exception);
    }

    // ── sort: / limit: ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Sort_DefaultsToDescending()
    {
        var q = Parse("sort:size");

        Assert.Equal(SortField.Size, q.Sort);
        Assert.Equal(SortDirection.Descending, q.SortDirection);
    }

    [Fact]
    public void Parse_Sort_FollowedByAsc_SetsDirection()
    {
        var q = Parse("sort:name asc");

        Assert.Equal(SortField.Name, q.Sort);
        Assert.Equal(SortDirection.Ascending, q.SortDirection);
    }

    [Fact]
    public void Parse_Limit_ParsesAsInt()
    {
        var q = Parse("limit:50");
        Assert.Equal(50, q.Limit);
    }

    [Fact]
    public void Parse_Limit_DefaultsTo200WhenAbsent()
    {
        var q = Parse("resume");
        Assert.Equal(200, q.Limit);
    }

    [Fact]
    public void Parse_Limit_IgnoresNonPositiveValues()
    {
        var q = Parse("limit:0");
        Assert.Equal(200, q.Limit); // stayed at the default
    }

    // ── Combined, realistic queries ──────────────────────────────────────────

    [Fact]
    public void Parse_RealisticCombinedQuery()
    {
        var q = Parse(@"ext:pdf,docx path:Downloads size:>1mb modified:<30d NOT ext:tmp limit:25");

        Assert.Equal(["pdf", "docx"], q.Extensions);
        Assert.Equal("Downloads", q.PathContains);
        Assert.Equal(1024L * 1024, q.Size!.MinBytes);
        Assert.Equal(Now.AddDays(-30), q.Modified!.After);
        Assert.Equal(["tmp"], q.ExcludedExtensions);
        Assert.Equal(25, q.Limit);
    }

    [Fact]
    public void Parse_EmptyQuery_ReturnsAllDefaults()
    {
        var q = Parse("");

        Assert.Empty(q.NameTerms);
        Assert.Null(q.Size);
        Assert.Equal(200, q.Limit);
        Assert.Equal(SortField.Relevance, q.Sort);
    }

    [Fact]
    public void Parse_QuotedPhraseWithSpaces_StaysOneToken()
    {
        var q = Parse("content:\"multi word phrase\" ext:txt");

        Assert.Equal("multi word phrase", q.ContentContains);
        Assert.Equal(["txt"], q.Extensions);
    }
}
