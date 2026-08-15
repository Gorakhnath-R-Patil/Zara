using Grpc.Core;
using GrpcDotNetNamedPipes;
using Zara.Contracts.Admin;
using Zara.Contracts.Indexing;
using Zara.Contracts.Journal;
using Zara.Contracts.Search;

namespace Zara.EngineClient.Client;

/// <inheritdoc cref="IEngineClient"/>
public sealed class EngineClient : IEngineClient
{
    private readonly NamedPipeChannel _channel;
    private volatile EngineConnectionState _state = EngineConnectionState.Connecting;

    public EngineClient(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _channel = new NamedPipeChannel(".", pipeName);
    }

    public EngineConnectionState State => _state;

    public event EventHandler<EngineConnectionState>? StateChanged;

    public async Task<bool> TryPingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = new AdminService.AdminServiceClient(_channel);
            await client.PingAsync(new PingRequest(), deadline: DateTime.UtcNow.AddSeconds(2), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            SetState(EngineConnectionState.Connected);
            return true;
        }
        catch (RpcException)
        {
            SetState(EngineConnectionState.Degraded);
            return false;
        }
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query, int maxResults = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = new SearchService.SearchServiceClient(_channel);
            var results = new List<SearchHit>();

            using var call = client.Search(new SearchRequest { Query = query, MaxResults = maxResults }, cancellationToken: cancellationToken);
            while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                results.Add(call.ResponseStream.Current);
            }

            SetState(EngineConnectionState.Connected);
            return results;
        }
        catch (RpcException)
        {
            // Degraded, not thrown: the caller (the App's file browser)
            // keeps working off direct filesystem enumeration regardless of
            // whether the index is reachable — ARCHITECTURE.md §9.4's
            // degraded-mode banner, and exactly what T40 verifies.
            SetState(EngineConnectionState.Degraded);
            return [];
        }
    }

    public async Task<IndexStatusResponse?> GetIndexStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = new IndexService.IndexServiceClient(_channel);
            var response = await client.GetStatusAsync(new GetStatusRequest(), cancellationToken: cancellationToken).ConfigureAwait(false);
            SetState(EngineConnectionState.Connected);
            return response;
        }
        catch (RpcException)
        {
            SetState(EngineConnectionState.Degraded);
            return null;
        }
    }

    public async Task<IReadOnlyList<OperationSummary>> GetUndoableAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = new JournalService.JournalServiceClient(_channel);
            var results = new List<OperationSummary>();

            using var call = client.GetUndoable(new GetUndoableRequest { Limit = limit }, cancellationToken: cancellationToken);
            while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                results.Add(call.ResponseStream.Current);
            }

            SetState(EngineConnectionState.Connected);
            return results;
        }
        catch (RpcException)
        {
            SetState(EngineConnectionState.Degraded);
            return [];
        }
    }

    private void SetState(EngineConnectionState newState)
    {
        if (_state == newState)
        {
            return;
        }

        _state = newState;
        StateChanged?.Invoke(this, newState);
    }
}
