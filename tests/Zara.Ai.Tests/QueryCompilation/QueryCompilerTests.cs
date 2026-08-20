using Zara.Ai.Providers;
using Zara.Ai.QueryCompilation;

namespace Zara.Ai.Tests.QueryCompilation;

/// <summary>
/// Runs the actual grammar-constrained compilation against a REAL, live
/// <c>gemma3:4b</c> — the concrete claim ARCHITECTURE.md §14.3 makes ("a 4B
/// model becomes reliable because it cannot emit invalid output") tested for
/// real rather than assumed, on the target hardware class this session
/// confirmed is available (§7.1's RTX 3050 4GB scenario). Same self-skip
/// discipline as <c>OllamaProviderTests</c>: honest about depending on
/// external infrastructure, not mocked into looking otherwise.
/// </summary>
public class QueryCompilerTests
{
    private readonly OllamaProvider _llm = new("gemma3:4b");
    private QueryCompiler Sut => new(_llm);

    [Fact]
    public async Task CompileAsync_ClearExtensionRequest_ProducesTheExpectedExtensionFilter()
    {
        if (!await _llm.IsAvailableAsync()) return;

        var result = await Sut.CompileAsync("find all pdf files");

        Assert.True(result.Success, $"Compilation failed: {result.ErrorMessage}; clarify={result.ClarifyQuestion}");
        Assert.Contains("pdf", result.Query!.Extensions.Select(e => e.ToLowerInvariant()));
    }

    [Fact]
    public async Task CompileAsync_SizeRequest_ProducesASizeFilter()
    {
        if (!await _llm.IsAvailableAsync()) return;

        var result = await Sut.CompileAsync("show me files larger than 500mb");

        Assert.True(result.Success, $"Compilation failed: {result.ErrorMessage}; clarify={result.ClarifyQuestion}");
        Assert.NotNull(result.Query!.Size);
        Assert.True(result.Query.Size!.MinBytes is > 0, $"Expected a positive min size, got {result.Query.Size.MinBytes}.");
    }

    [Fact]
    public async Task CompileAsync_ImageTypeRequest_ProducesAnImageTypeFilter()
    {
        if (!await _llm.IsAvailableAsync()) return;

        var result = await Sut.CompileAsync("find my vacation photos");

        Assert.True(result.Success, $"Compilation failed: {result.ErrorMessage}; clarify={result.ClarifyQuestion}");
        // Accept either a type-class match or a name-term match — both are
        // reasonable readings of "photos", and pinning the assertion to one
        // exact interpretation would make this test brittle against a
        // small model's legitimate phrasing variance.
        bool matchedImage = result.Query!.TypeClasses.Any(t => t.Equals("image", StringComparison.OrdinalIgnoreCase));
        bool matchedKeyword = result.Query.NameTerms.Any(k => k.Contains("photo", StringComparison.OrdinalIgnoreCase) || k.Contains("vacation", StringComparison.OrdinalIgnoreCase));
        Assert.True(matchedImage || matchedKeyword, $"Neither type_class nor keywords reflected 'photos': types=[{string.Join(",", result.Query.TypeClasses)}] keywords=[{string.Join(",", result.Query.NameTerms)}]");
    }

    [Fact]
    public async Task CompileAsync_OutputNeverContainsARawFilePath()
    {
        if (!await _llm.IsAvailableAsync()) return;

        // The concrete security property §14.3 claims schema design buys:
        // path_scope is a closed enum, so the model has no channel to emit
        // a literal path even if asked to.
        var result = await Sut.CompileAsync(@"find files in C:\Users\bob\Documents\secret");

        if (result.Success)
        {
            Assert.NotNull(result.Query);
            // InScope, if set at all, must be one of the known folder
            // names QueryCompiler maps to — never the raw path text.
            if (result.Query!.InScope is { } scope)
            {
                Assert.DoesNotContain(@"\", scope);
                Assert.DoesNotContain(":", scope);
            }
        }
        // A low-confidence/clarify outcome is also an acceptable, safe result
        // for an ambiguous/unusual request — the only unacceptable outcome
        // is a raw path leaking into the query, checked above when successful.
    }

    [Fact]
    public async Task CompileAsync_ProducesAConfidenceScoreWithinRange()
    {
        if (!await _llm.IsAvailableAsync()) return;

        var result = await Sut.CompileAsync("find large video files from last month");

        Assert.InRange(result.Confidence, 0.0, 1.0);
    }
}
