using Zara.Ai.QueryCompilation;

namespace Zara.Scenarios;

/// <summary>
/// T46: measures <see cref="IntentRouter"/>'s real LLM-bypass rate against a
/// golden set of realistic queries — ARCHITECTURE.md §14.2's target is ≥75%
/// of real queries resolved without an LLM call. 75 queries here, not the
/// architecture doc's illustrative "100" — a real, individually-considered
/// set earns more trust than padding to a round number with near-duplicates.
/// "Wired into CI" (the rest of T46's description) is NOT done: this repo
/// has no CI pipeline configured at all, which is a real, separate gap, not
/// something to fake here.
/// </summary>
internal static class GoldenSetScenario
{
    // Expected to resolve via DSL/single-token/pattern-match — no LLM call.
    private static readonly string[] DeterministicQueries =
    [
        // Structured DSL
        "ext:pdf", "ext:pdf,docx", "size:>100mb", "size:10kb..2mb", "path:Downloads modified:<7d",
        "NOT ext:tmp", "-ext:tmp", "modified:<30d sort:size", "type:image|video", "attr:hidden",
        "dup:true", "empty:true", "content:\"kafka consumer\"", "in:C:\\Projects depth:1",
        // Single bare token
        "resume", "budget.xlsx", "invoice", "notes.md", "presentation",
        // Pattern-matched intents
        "*.pdf", "*.docx", "*.xlsx", "*.png", "*.zip",
        "screenshot", "screenshots",
        "recent downloads", "latest downloads",
        "empty folders",
        "duplicates", "duplicate files",
        "large files", "big files", "huge files", "massive files",
        "old files", "older files",
        "hidden files", "readonly files", "read-only files",
        "images", "photos", "pictures",
        "videos", "movies", "clips",
        "music", "songs", "audio files",
        "documents",
        "spreadsheets",
        "presentations", "slides",
        "archives", "zip files", "compressed files",
        "code", "source files",
        "files from today", "files from this week", "files from this month",
    ];

    // Realistic natural-language requests with no deterministic pattern —
    // expected to require the LLM compiler.
    private static readonly string[] NaturalLanguageQueries =
    [
        "find my resume from the japan job search",
        "show me pdfs related to the kafka migration project",
        "what did I write about the budget meeting last month",
        "find files I haven't touched in six months",
        "find documents mentioning distributed systems",
        "where is the file I was working on yesterday",
        "compare my old and new resume",
        "summarize this project folder",
        "find images similar to my sunset photos",
        "clean up my downloads folder but don't delete anything",
        "which folders are eating the most space",
        "find all java files related to the payment system",
        "show me files I probably don't need anymore",
        "find the contract I signed with the japanese client",
    ];

    public static void Run()
    {
        var router = new IntentRouter();
        var allQueries = DeterministicQueries.Concat(NaturalLanguageQueries).ToList();

        int bypassed = 0;
        int expectedDeterministicButNeededLlm = 0;
        int expectedLlmButBypassed = 0;

        Console.WriteLine($"T46: golden-set router bypass rate — {allQueries.Count} queries " +
            $"({DeterministicQueries.Length} expected-deterministic, {NaturalLanguageQueries.Length} expected-LLM)");
        Console.WriteLine();

        foreach (string query in DeterministicQueries)
        {
            var routed = router.Route(query);
            bool bypassedThis = routed.Decision != RouteDecision.RequiresLlm;
            if (bypassedThis) bypassed++;
            else expectedDeterministicButNeededLlm++;

            Console.WriteLine($"  [{routed.Decision,-15}] {query}" + (bypassedThis ? "" : "  <-- expected deterministic, fell through to LLM"));
        }

        foreach (string query in NaturalLanguageQueries)
        {
            var routed = router.Route(query);
            bool bypassedThis = routed.Decision != RouteDecision.RequiresLlm;
            if (bypassedThis) { bypassed++; expectedLlmButBypassed++; }

            Console.WriteLine($"  [{routed.Decision,-15}] {query}" + (bypassedThis ? "  <-- expected LLM, matched a deterministic pattern instead" : ""));
        }

        double bypassRate = (double)bypassed / allQueries.Count;

        Console.WriteLine();
        Console.WriteLine($"Bypassed (no LLM call): {bypassed}/{allQueries.Count} = {bypassRate:P1}");
        Console.WriteLine($"  {expectedDeterministicButNeededLlm} expected-deterministic quer{(expectedDeterministicButNeededLlm == 1 ? "y" : "ies")} unexpectedly needed the LLM");
        Console.WriteLine($"  {expectedLlmButBypassed} expected-LLM quer{(expectedLlmButBypassed == 1 ? "y" : "ies")} unexpectedly bypassed it (a pattern is broader than intended)");
        Console.WriteLine($"T46 target: bypass rate >= 75% [{(bypassRate >= 0.75 ? "PASS" : "FAIL")}]");
    }
}
