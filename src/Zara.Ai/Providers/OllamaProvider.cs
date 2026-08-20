using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Zara.Ai.Providers;

/// <inheritdoc cref="ILlmProvider"/>
/// <remarks>
/// Talks to Ollama's HTTP API (<c>/api/generate</c>) directly — no SDK
/// dependency; the request/response shape is small and stable enough that a
/// third-party client wrapper would add a dependency without removing real
/// risk, unlike <c>IFileOperation</c> (T31) or the named-pipe gRPC transport
/// (T37), where the underlying surface was genuinely complex or
/// vtable-fragile. <c>format</c> carries the JSON Schema for structured
/// output (Ollama ≥0.5); <c>keep_alive</c> defaults to 30 minutes per §7.1.
/// </remarks>
public sealed class OllamaProvider : ILlmProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public OllamaProvider(string modelId, string baseUrl = "http://localhost:11434", HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ModelId = modelId;

        if (httpClient is not null)
        {
            _http = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            _ownsHttpClient = true;
        }
    }

    public string ModelId { get; }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("/api/version", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();

        var options = new Dictionary<string, object?>
        {
            ["temperature"] = request.Temperature,
            ["num_predict"] = request.NumPredict,
            ["num_ctx"] = request.NumCtx,
        };
        if (request.Seed is { } seed)
        {
            options["seed"] = seed;
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = ModelId,
            ["system"] = request.SystemPrompt,
            ["prompt"] = request.UserPrompt,
            ["stream"] = false,
            ["options"] = options,
            ["keep_alive"] = FormatKeepAlive(request.KeepAlive ?? TimeSpan.FromMinutes(30)),
        };

        if (request.JsonSchema is not null)
        {
            using var schemaDoc = JsonDocument.Parse(request.JsonSchema);
            payload["format"] = schemaDoc.RootElement.Clone();
        }

        try
        {
            using var httpContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var httpResponse = await _http.PostAsync("/api/generate", httpContent, cancellationToken).ConfigureAwait(false);

            string body = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                return new LlmResponse(false, string.Empty, $"Ollama returned {(int)httpResponse.StatusCode}: {body}", stopwatch.Elapsed);
            }

            using var doc = JsonDocument.Parse(body);
            string responseText = doc.RootElement.TryGetProperty("response", out var responseProp)
                ? responseProp.GetString() ?? string.Empty
                : string.Empty;

            return new LlmResponse(true, responseText, null, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new LlmResponse(false, string.Empty, ex.Message, stopwatch.Elapsed);
        }
    }

    private static string FormatKeepAlive(TimeSpan keepAlive) => $"{(int)keepAlive.TotalSeconds}s";

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
