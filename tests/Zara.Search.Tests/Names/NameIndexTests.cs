using Zara.Core.Files;
using Zara.Search.Names;

namespace Zara.Search.Tests.Names;

public class NameIndexTests
{
    private readonly NameIndex _sut = new();

    private static FileId Id(long frn) => new(VolumeId: 1, Frn: frn);

    // ── Basic upsert / search / remove ──────────────────────────────────────

    [Fact]
    public void Search_ExactMatch_IsClassifiedExact()
    {
        _sut.Upsert(Id(1), "resume.pdf");

        var hits = _sut.Search("resume.pdf");

        Assert.Equal(NameMatchKind.Exact, Assert.Single(hits).MatchKind);
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        _sut.Upsert(Id(1), "Resume.PDF");

        var hits = _sut.Search("resume.pdf");

        Assert.Single(hits);
    }

    [Fact]
    public void Search_PrefixMatch_RanksAbovePlainSubstring()
    {
        _sut.Upsert(Id(1), "japan_notes.txt");   // substring: "japan" appears, not at start
        _sut.Upsert(Id(2), "japan.txt");          // prefix: starts with "japan"

        var hits = _sut.Search("japan");

        Assert.Equal(2, hits.Count);
        Assert.Equal("japan.txt", hits[0].Name);
        Assert.Equal(NameMatchKind.Prefix, hits[0].MatchKind);
    }

    [Fact]
    public void Search_WordBoundaryMatch_RanksAboveSubstring()
    {
        _sut.Upsert(Id(1), "myjapantrip.txt");     // substring, no boundary before "japan"
        _sut.Upsert(Id(2), "my-japan-trip.txt");   // word boundary: '-' before "japan"

        var hits = _sut.Search("japan");

        Assert.Equal("my-japan-trip.txt", hits[0].Name);
        Assert.Equal(NameMatchKind.WordBoundary, hits[0].MatchKind);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        _sut.Upsert(Id(1), "resume.pdf");

        Assert.Empty(_sut.Search("nonexistent"));
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        Assert.Empty(_sut.Search("anything"));
    }

    [Fact]
    public void Remove_EntryNoLongerAppearsInSearch()
    {
        _sut.Upsert(Id(1), "resume.pdf");
        _sut.Remove(Id(1));

        Assert.Empty(_sut.Search("resume"));
        Assert.Equal(0, _sut.Count);
    }

    [Fact]
    public void Remove_NonexistentId_IsANoOp()
    {
        var exception = Record.Exception(() => _sut.Remove(Id(999)));
        Assert.Null(exception);
    }

    [Fact]
    public void Upsert_SameIdTwice_UpdatesRatherThanDuplicates()
    {
        _sut.Upsert(Id(1), "old-name.txt");
        _sut.Upsert(Id(1), "new-name.txt");

        Assert.Equal(1, _sut.Count);
        Assert.Empty(_sut.Search("old-name"));
        Assert.Single(_sut.Search("new-name"));
    }

    [Fact]
    public void Upsert_ReusesASlotFreedByRemove_WithoutCrossContamination()
    {
        _sut.Upsert(Id(1), "first.txt");
        _sut.Remove(Id(1));
        _sut.Upsert(Id(2), "second.txt");

        Assert.Empty(_sut.Search("first"));
        Assert.Single(_sut.Search("second"));
        Assert.Equal(1, _sut.Count);
    }

    // ── Incremental build (T18: "search works during first scan") ──────────

    [Fact]
    public void Search_ReflectsWhateverHasBeenUpsertedSoFar_MidBuild()
    {
        _sut.Upsert(Id(1), "alpha.txt");
        Assert.Single(_sut.Search("alpha"));
        Assert.Empty(_sut.Search("beta"));

        _sut.Upsert(Id(2), "beta.txt");
        Assert.Single(_sut.Search("beta"));

        _sut.Upsert(Id(3), "gamma.txt");
        Assert.Equal(3, _sut.Count);
    }

    // ── Multi-token queries (implicit AND) ──────────────────────────────────

    [Fact]
    public void Search_MultiWordQuery_RequiresAllTokensToMatch()
    {
        _sut.Upsert(Id(1), "japan_resume_2024.pdf");
        _sut.Upsert(Id(2), "japan_photos.jpg");

        var hits = _sut.Search("japan resume");

        Assert.Single(hits);
        Assert.Equal("japan_resume_2024.pdf", hits[0].Name);
    }

    [Fact]
    public void Search_MultiWordQuery_NonContiguousTokens_StillMatchAsSubstringTier()
    {
        _sut.Upsert(Id(1), "resume_for_japan_job.pdf");

        var hits = _sut.Search("japan resume");

        Assert.Single(hits);
        Assert.Equal(NameMatchKind.Substring, hits[0].MatchKind);
    }

    // ── Short tokens (below the 3-char trigram floor) ───────────────────────

    [Fact]
    public void Search_ShortToken_FallsBackToFullScan_StillFindsMatches()
    {
        _sut.Upsert(Id(1), "ab.txt");
        _sut.Upsert(Id(2), "xy.txt");

        var hits = _sut.Search("ab");

        Assert.Single(hits);
        Assert.Equal("ab.txt", hits[0].Name);
    }

    [Fact]
    public void Search_SingleCharToken_Works()
    {
        _sut.Upsert(Id(1), "z.txt");
        _sut.Upsert(Id(2), "y.txt");

        Assert.Single(_sut.Search("z"));
    }

    // ── Trigram false-positive verification ─────────────────────────────────

    [Fact]
    public void Search_TrigramFalsePositive_IsFilteredOut()
    {
        // "abcabc" contains trigrams {abc, bca, cab} — the same trigram SET
        // as a name built from those trigrams in a different arrangement,
        // e.g. "cabcab", without "abcabc" itself appearing as a substring of
        // either. This exercises the ordinal-verification step directly.
        _sut.Upsert(Id(1), "cabcab.txt");

        var hits = _sut.Search("abcabc");

        Assert.Empty(hits);
    }

    // ── maxResults ───────────────────────────────────────────────────────────

    [Fact]
    public void Search_RespectsMaxResults()
    {
        for (int i = 0; i < 20; i++)
        {
            _sut.Upsert(Id(i), $"file-{i}.txt");
        }

        var hits = _sut.Search("file", maxResults: 5);

        Assert.Equal(5, hits.Count);
    }

    [Fact]
    public void Search_MaxResultsZeroOrNegative_ReturnsEmpty()
    {
        _sut.Upsert(Id(1), "file.txt");

        Assert.Empty(_sut.Search("file", maxResults: 0));
        Assert.Empty(_sut.Search("file", maxResults: -1));
    }

    // ── Scale sanity (not a perf benchmark — that's T21) ────────────────────

    [Fact]
    public void Search_ScalesTo10000Entries_AndFindsTheRightOnes()
    {
        for (int i = 0; i < 10_000; i++)
        {
            _sut.Upsert(Id(i), $"document-{i:D5}.txt");
        }
        _sut.Upsert(Id(99_999), "needle.txt");

        var hits = _sut.Search("needle");

        Assert.Single(hits);
        Assert.Equal(10_001, _sut.Count);
    }
}
