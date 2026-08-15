using Zara.Contracts.Indexing;
using Zara.Contracts.Journal;
using Zara.Contracts.Search;

namespace Zara.EngineClient.Client;

public enum EngineConnectionState { Connecting, Connected, Degraded }

/// <summary>
/// The App-side typed gRPC client — ARCHITECTURE.md §8.1's
/// "EngineClient... + reconnect/backoff/degraded mode". Every RPC method
/// here is failure-safe by design: a call that fails because the Engine is
/// unreachable sets <see cref="State"/> to <see cref="EngineConnectionState.Degraded"/>
/// and returns an empty/null result rather than throwing or blocking —
/// this is the actual mechanism behind "kill Engine mid-search → App keeps
/// browsing" (§28 #3, T40).
/// </summary>
public interface IEngineClient
{
    EngineConnectionState State { get; }

    event EventHandler<EngineConnectionState>? StateChanged;

    /// <summary>A lightweight liveness probe — used both to detect recovery
    /// while degraded and (by a caller like <c>EngineProcessManager</c>'s
    /// consumer) to decide when it's safe to stop retrying a restart.</summary>
    Task<bool> TryPingAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults = 50, CancellationToken cancellationToken = default);

    Task<IndexStatusResponse?> GetIndexStatusAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationSummary>> GetUndoableAsync(int limit = 50, CancellationToken cancellationToken = default);
}
