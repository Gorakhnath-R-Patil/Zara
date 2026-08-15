using Grpc.Core;
using GrpcDotNetNamedPipes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zara.Contracts.Admin;
using Zara.Contracts.Indexing;
using Zara.Contracts.Journal;
using Zara.Contracts.Search;
using Zara.Core.Files;
using Zara.Engine;
using Zara.Search.Names;

namespace Zara.Engine.Tests;

/// <summary>
/// Starts a REAL Engine host, listening on a real (uniquely-named, so
/// parallel test runs don't collide) named pipe, and drives it with a real
/// gRPC client — the only way to actually prove the DI wiring, the proto
/// codegen, and the named-pipe transport all agree with each other.
/// </summary>
public class EngineHostTests : IAsyncLifetime
{
    private readonly string _pipeName = $"zara-engine-test-{Guid.NewGuid():N}";
    private readonly string _dbPath;
    private IHost _host = null!;

    public EngineHostTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-engine-test-{Guid.NewGuid():N}.db");
    }

    public async Task InitializeAsync()
    {
        _host = EngineHost.Build([], _dbPath, _pipeName);
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    private NamedPipeChannel OpenChannel() => new(".", _pipeName);

    [Fact]
    public async Task Admin_Ping_ReturnsARealProcessId()
    {
        var channel = OpenChannel();
        var client = new AdminService.AdminServiceClient(channel);

        var response = await client.PingAsync(new PingRequest());

        Assert.Equal(Environment.ProcessId, response.ProcessId);
        Assert.True(response.UptimeSeconds >= 0);
    }

    [Fact]
    public async Task Search_StreamsRealResultsFromTheSharedNameIndex()
    {
        // Seed the SAME NameIndex instance the running host is using —
        // proves the DI registration is actually singleton-shared with the
        // gRPC service, not a second, disconnected instance.
        var nameIndex = _host.Services.GetRequiredService<INameIndex>();
        nameIndex.Upsert(new FileId(1, 1), "resume.pdf");
        nameIndex.Upsert(new FileId(1, 2), "photo.jpg");

        var channel = OpenChannel();
        var client = new SearchService.SearchServiceClient(channel);

        var hits = new List<SearchHit>();
        using var call = client.Search(new SearchRequest { Query = "resume", MaxResults = 10 });
        while (await call.ResponseStream.MoveNext())
        {
            hits.Add(call.ResponseStream.Current);
        }

        var onlyHit = Assert.Single(hits);
        Assert.Equal("resume.pdf", onlyHit.Name);
    }

    [Fact]
    public async Task Search_EmptyQuery_StreamsNothing_DoesNotThrow()
    {
        var channel = OpenChannel();
        var client = new SearchService.SearchServiceClient(channel);

        var hits = new List<SearchHit>();
        using var call = client.Search(new SearchRequest { Query = "" });
        while (await call.ResponseStream.MoveNext())
        {
            hits.Add(call.ResponseStream.Current);
        }

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Index_GetStatus_ReflectsTheSharedNameIndexCount()
    {
        var nameIndex = _host.Services.GetRequiredService<INameIndex>();
        nameIndex.Upsert(new FileId(1, 1), "a.txt");
        nameIndex.Upsert(new FileId(1, 2), "b.txt");
        nameIndex.Upsert(new FileId(1, 3), "c.txt");

        var channel = OpenChannel();
        var client = new IndexService.IndexServiceClient(channel);

        var status = await client.GetStatusAsync(new GetStatusRequest());

        Assert.Equal(3, status.IndexedFileCount);
        Assert.False(status.IsScanning);
    }

    [Fact]
    public async Task Journal_GetUndoable_ReturnsEmptyStream_WhenNothingIsJournaledYet()
    {
        var channel = OpenChannel();
        var client = new JournalService.JournalServiceClient(channel);

        var operations = new List<OperationSummary>();
        using var call = client.GetUndoable(new GetUndoableRequest { Limit = 10 });
        while (await call.ResponseStream.MoveNext())
        {
            operations.Add(call.ResponseStream.Current);
        }

        Assert.Empty(operations);
    }

    [Fact]
    public async Task MultipleClients_CanConnectConcurrently()
    {
        var channelA = OpenChannel();
        var channelB = OpenChannel();
        var clientA = new AdminService.AdminServiceClient(channelA);
        var clientB = new AdminService.AdminServiceClient(channelB);

        var responses = await Task.WhenAll(
            clientA.PingAsync(new PingRequest()).ResponseAsync,
            clientB.PingAsync(new PingRequest()).ResponseAsync);

        Assert.All(responses, r => Assert.Equal(Environment.ProcessId, r.ProcessId));
    }
}
