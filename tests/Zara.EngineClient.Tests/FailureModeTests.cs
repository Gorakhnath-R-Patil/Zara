using Zara.EngineClient.Client;
using Zara.EngineClient.Process;

namespace Zara.EngineClient.Tests;

/// <summary>
/// T40 — ARCHITECTURE.md §28 #3 / §9.4: "kill Engine mid-search → App keeps
/// browsing". This is the one test in this whole session that most directly
/// exercises the entire point of the two-process split (§9.2): a crashed
/// Engine must never crash, hang, or corrupt the caller. A real spawned
/// Engine process is force-killed (`Process.Kill()`, no clean shutdown —
/// the closest thing to `kill -9` available cross-process on Windows), and
/// the client is asserted to survive that cleanly.
/// </summary>
public class FailureModeTests : IAsyncDisposable
{
    private static string EngineExePath => Path.Combine(AppContext.BaseDirectory, "Zara.Engine.exe");

    private EngineProcessManager? _manager;

    public async ValueTask DisposeAsync()
    {
        if (_manager is not null)
        {
            await _manager.DisposeAsync();
        }
    }

    [Fact]
    public async Task KillEngineMidSearch_ClientSurvivesCleanly_AndReportsDegraded()
    {
        string pipeName = $"zara-failuremode-test-{Guid.NewGuid():N}";
        string dbPath = Path.Combine(Path.GetTempPath(), $"zara-failuremode-test-{Guid.NewGuid():N}.db");
        _manager = new EngineProcessManager(EngineExePath, $"--pipe-name={pipeName} --db-path=\"{dbPath}\"");
        await _manager.StartAsync();

        var client = new Client.EngineClient(pipeName);
        Assert.True(await WaitForReachableAsync(client), "Engine never became reachable before the kill.");

        // Confirm search genuinely works BEFORE the kill — otherwise a
        // "survives the kill" result would be meaningless.
        await client.SearchAsync("anything");
        Assert.Equal(EngineConnectionState.Connected, client.State);

        // The kill: forceful, no IPC handshake, no clean shutdown.
        System.Diagnostics.Process.GetProcessById(_manager.ProcessId!.Value).Kill();
        await Task.Delay(500); // let the OS actually tear the pipe down

        // The property under test: this call must not throw, and must not
        // hang indefinitely (xUnit's own timeout would catch a true hang).
        var exception = await Record.ExceptionAsync(async () => await client.SearchAsync("anything"));

        Assert.Null(exception);
        Assert.Equal(EngineConnectionState.Degraded, client.State);
    }

    [Fact]
    public async Task KillEngineMidSearch_RepeatedCallsAfterTheKill_AllFailSafely_NeverThrow()
    {
        string pipeName = $"zara-failuremode-test-{Guid.NewGuid():N}";
        string dbPath = Path.Combine(Path.GetTempPath(), $"zara-failuremode-test-{Guid.NewGuid():N}.db");
        _manager = new EngineProcessManager(EngineExePath, $"--pipe-name={pipeName} --db-path=\"{dbPath}\"");
        await _manager.StartAsync();

        var client = new Client.EngineClient(pipeName);
        await WaitForReachableAsync(client);

        System.Diagnostics.Process.GetProcessById(_manager.ProcessId!.Value).Kill();
        await Task.Delay(500);

        // Simulates a user continuing to type/search in a degraded App —
        // every subsequent call must keep failing safely, not just the first.
        for (int i = 0; i < 5; i++)
        {
            var results = await client.SearchAsync($"query-{i}");
            Assert.Empty(results);
        }

        Assert.Equal(EngineConnectionState.Degraded, client.State);
    }

    [Fact]
    public async Task AfterEngineRestarts_ClientRecoversToConnected()
    {
        string pipeName = $"zara-recover-test-{Guid.NewGuid():N}";
        string dbPath = Path.Combine(Path.GetTempPath(), $"zara-recover-test-{Guid.NewGuid():N}.db");
        _manager = new EngineProcessManager(EngineExePath, $"--pipe-name={pipeName} --db-path=\"{dbPath}\"");
        await _manager.StartAsync();

        var client = new Client.EngineClient(pipeName);
        await WaitForReachableAsync(client);

        System.Diagnostics.Process.GetProcessById(_manager.ProcessId!.Value).Kill();
        await Task.Delay(500);
        await client.TryPingAsync();
        Assert.Equal(EngineConnectionState.Degraded, client.State);

        // "Restart": a fresh Engine process on the same pipe name — exactly
        // what EngineProcessManager's own backoff-driven restart (once wired
        // to ReconnectBackoff by its future caller) would do.
        await _manager.DisposeAsync();
        _manager = new EngineProcessManager(EngineExePath, $"--pipe-name={pipeName} --db-path=\"{dbPath}\"");
        await _manager.StartAsync();

        bool recovered = await WaitForReachableAsync(client);

        Assert.True(recovered);
        Assert.Equal(EngineConnectionState.Connected, client.State);
    }

    private static async Task<bool> WaitForReachableAsync(Client.EngineClient client)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if (await client.TryPingAsync())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }
}
