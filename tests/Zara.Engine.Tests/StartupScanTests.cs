using GrpcDotNetNamedPipes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zara.Contracts.Indexing;
using Zara.Contracts.Search;
using Zara.Engine;

namespace Zara.Engine.Tests;

/// <summary>
/// T39's actual proof: real files on a real temp directory, scanned by a
/// real <c>ScanOrchestrator</c> running inside a real Engine host, found by
/// a real gRPC <c>Search</c> call — not a NameIndex seeded by hand (every
/// other Engine test upserts directly; this is the one that proves the
/// scan-to-search pipeline is actually wired end to end).
/// </summary>
public class StartupScanTests : IAsyncLifetime
{
    private readonly string _pipeName = $"zara-startupscan-test-{Guid.NewGuid():N}";
    private readonly string _dbPath;
    private readonly string _scanRoot;
    private IHost _host = null!;

    public StartupScanTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"zara-startupscan-test-{Guid.NewGuid():N}.db");

        // Deliberately NOT under Path.GetTempPath() (excluded by
        // DefaultSkipList's real Temp-folder rule — the same gotcha hit
        // WalkScannerTests/ScanOrchestratorTests/T16's benchmark earlier
        // this session) and NOT under AppContext.BaseDirectory either: a
        // test assembly's own output directory is itself a "bin\Debug\..."
        // path, which matches DefaultSkipList's ["bin","debug"] rule (meant
        // to exclude OTHER projects' .NET build output when scanning real
        // source trees — ARCHITECTURE.md §10.5) — found by adding
        // Diagnostic_FilesActuallyExistOnDisk, which proved the files exist
        // and NtDirectoryEnumerator sees them directly, isolating the bug
        // to skip-list matching rather than enumeration. A drive-root
        // location avoids every hard-exclusion pattern.
        _scanRoot = Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory) ?? @"C:\", "ZaraEngineTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scanRoot);

        File.WriteAllText(Path.Combine(_scanRoot, "resume.pdf"), "not a real pdf, just test content");
        File.WriteAllText(Path.Combine(_scanRoot, "photo.jpg"), "not a real jpg either");
        Directory.CreateDirectory(Path.Combine(_scanRoot, "subfolder"));
        File.WriteAllText(Path.Combine(_scanRoot, "subfolder", "budget.xlsx"), "nested file");
    }

    public async Task InitializeAsync()
    {
        _host = EngineHost.Build([], _dbPath, _pipeName, _scanRoot);
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

        try { Directory.Delete(_scanRoot, recursive: true); } catch (IOException) { }
    }

    private NamedPipeChannel OpenChannel() => new(".", _pipeName);

    [Fact]
    public async Task StartupScan_ThenSearch_FindsARealFileFromDisk()
    {
        var indexClient = new IndexService.IndexServiceClient(OpenChannel());
        Assert.True(await WaitForScanToFinishAsync(indexClient), "The startup scan never finished.");

        var searchClient = new SearchService.SearchServiceClient(OpenChannel());
        var hits = new List<SearchHit>();
        using var call = searchClient.Search(new SearchRequest { Query = "resume", MaxResults = 10 });
        while (await call.ResponseStream.MoveNext(default))
        {
            hits.Add(call.ResponseStream.Current);
        }

        Assert.Contains(hits, h => h.Name == "resume.pdf");
    }

    [Fact]
    public async Task StartupScan_FindsNestedFilesToo()
    {
        var indexClient = new IndexService.IndexServiceClient(OpenChannel());
        await WaitForScanToFinishAsync(indexClient);

        var searchClient = new SearchService.SearchServiceClient(OpenChannel());
        var hits = new List<SearchHit>();
        using var call = searchClient.Search(new SearchRequest { Query = "budget", MaxResults = 10 });
        while (await call.ResponseStream.MoveNext(default))
        {
            hits.Add(call.ResponseStream.Current);
        }

        Assert.Contains(hits, h => h.Name == "budget.xlsx");
    }

    [Fact]
    public async Task StartupScan_IndexStatusReflectsRealFileCounts()
    {
        var indexClient = new IndexService.IndexServiceClient(OpenChannel());
        await WaitForScanToFinishAsync(indexClient);

        var status = await indexClient.GetStatusAsync(new GetStatusRequest());

        // 2 root files + 1 subfolder (as its own entry) + 1 nested file = 4.
        Assert.Equal(4, status.IndexedFileCount);
        Assert.False(status.IsScanning);
    }

    private static async Task<bool> WaitForScanToFinishAsync(IndexService.IndexServiceClient client)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var status = await client.GetStatusAsync(new GetStatusRequest());
            if (!status.IsScanning && status.IndexedFileCount > 0)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }
}
