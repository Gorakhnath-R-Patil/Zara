using Zara.Ai.Providers;

namespace Zara.Ai.Tests.Providers;

/// <summary>
/// Runs against a REAL Ollama instance when one is reachable on
/// localhost:11434 with <c>gemma3:4b</c> pulled — both were genuinely true
/// in the environment this was written in (confirmed via a live GPU-detected
/// Ollama server before writing a line of this file). Every test checks
/// <see cref="OllamaProvider.IsAvailableAsync"/> first and self-skips
/// (returns without asserting) when it isn't — this suite is honest about
/// depending on external, stateful infrastructure this repo doesn't control,
/// rather than mocking the HTTP contract and calling that equivalent proof.
/// </summary>
public class OllamaProviderTests
{
    private const string Model = "gemma3:4b";
    private readonly OllamaProvider _sut = new(Model);

    [Fact]
    public async Task IsAvailableAsync_AgainstAReachableServer_ReturnsTrue()
    {
        // No skip here deliberately: if this returns false, every other
        // test in this class will self-skip silently, which would look like
        // a suite full of green checks that tested nothing. This one is the
        // canary — it's allowed to fail loudly.
        bool available = await _sut.IsAvailableAsync();

        Assert.True(available, "Ollama is not reachable on localhost:11434 — every other test in this class will self-skip.");
    }

    [Fact]
    public async Task CompleteAsync_PlainPrompt_ReturnsNonEmptyText()
    {
        if (!await _sut.IsAvailableAsync()) return;

        var response = await _sut.CompleteAsync(new LlmRequest(
            SystemPrompt: "You are a terse assistant. Answer in exactly one word.",
            UserPrompt: "What color is the sky on a clear day? One word only.",
            NumPredict: 10));

        Assert.True(response.Success, response.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(response.Content));
    }

    [Fact]
    public async Task CompleteAsync_WithJsonSchema_ReturnsValidJsonMatchingIt()
    {
        if (!await _sut.IsAvailableAsync()) return;

        const string schema = """
            {
              "type": "object",
              "required": ["color", "is_warm"],
              "additionalProperties": false,
              "properties": {
                "color": { "type": "string" },
                "is_warm": { "type": "boolean" }
              }
            }
            """;

        var response = await _sut.CompleteAsync(new LlmRequest(
            SystemPrompt: "Extract structured data from the user's message.",
            UserPrompt: "The sunset was a deep, warm orange.",
            JsonSchema: schema,
            NumPredict: 100));

        Assert.True(response.Success, response.ErrorMessage);

        // The actual proof: real, live, grammar-constrained output parses
        // as JSON with exactly the fields the schema demanded — not "the
        // HTTP call didn't throw", but "the model produced conforming output".
        using var doc = System.Text.Json.JsonDocument.Parse(response.Content);
        Assert.True(doc.RootElement.TryGetProperty("color", out _));
        Assert.True(doc.RootElement.TryGetProperty("is_warm", out var isWarm));
        Assert.True(isWarm.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False);
    }

    [Fact]
    public async Task CompleteAsync_UnreachableModel_FailsCleanlyRatherThanThrowing()
    {
        if (!await _sut.IsAvailableAsync()) return;

        // A model name that's syntactically valid but not pulled — the
        // provider's job is to surface this as a structured failure, the
        // same discipline ARCHITECTURE.md §16.3 requires of tool results.
        var sut = new OllamaProvider("this-model-does-not-exist:latest");

        var response = await sut.CompleteAsync(new LlmRequest("system", "user"));

        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
    }

    [Fact]
    public async Task IsAvailableAsync_WrongPort_ReturnsFalseRatherThanThrowing()
    {
        var sut = new OllamaProvider("irrelevant", baseUrl: "http://localhost:1");

        bool available = await sut.IsAvailableAsync();

        Assert.False(available);
    }
}
