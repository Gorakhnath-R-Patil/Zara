using Zara.EngineClient.Client;
using Zara.EngineClient.Process;

namespace Zara.EngineClient.Tests.Process;

/// <summary>
/// Spawns the REAL <c>Zara.Engine.exe</c> (copied into this project's own
/// output directory via the project reference — see the .csproj comment)
/// rather than a stand-in process, since job-object kill-on-close and
/// named-pipe reachability are exactly the properties that only mean
/// something against the real binary.
/// </summary>
public class EngineProcessManagerTests : IAsyncDisposable
{
    private static string EngineExePath => Path.Combine(AppContext.BaseDirectory, "Zara.Engine.exe");

    private readonly List<EngineProcessManager> _managers = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var manager in _managers)
        {
            await manager.DisposeAsync();
        }
    }

    private EngineProcessManager MakeManager(out string pipeName)
    {
        pipeName = $"zara-epm-test-{Guid.NewGuid():N}";
        string dbPath = Path.Combine(Path.GetTempPath(), $"zara-epm-test-{Guid.NewGuid():N}.db");
        var manager = new EngineProcessManager(EngineExePath, $"--pipe-name={pipeName} --db-path=\"{dbPath}\"");
        _managers.Add(manager);
        return manager;
    }

    [Fact]
    public async Task StartAsync_LaunchesARealRunningProcess()
    {
        var manager = MakeManager(out _);

        await manager.StartAsync();

        Assert.True(manager.IsRunning);
        Assert.NotNull(manager.ProcessId);
    }

    [Fact]
    public async Task StartAsync_TheSpawnedEngineBecomesReachableOverItsOwnPipe()
    {
        var manager = MakeManager(out string pipeName);
        await manager.StartAsync();

        var client = new Client.EngineClient(pipeName);
        bool reachable = await WaitForReachableAsync(client);

        Assert.True(reachable, "The spawned Engine never became reachable over its named pipe.");
    }

    [Fact]
    public async Task StopAsync_TerminatesTheProcess_AndDoesNotRaiseProcessExited()
    {
        var manager = MakeManager(out _);
        await manager.StartAsync();

        bool exitedRaised = false;
        manager.ProcessExited += (_, _) => exitedRaised = true;

        await manager.StopAsync();

        Assert.False(manager.IsRunning);
        Assert.False(exitedRaised, "A deliberate StopAsync must not be reported as a crash.");
    }

    [Fact]
    public async Task JobObject_DisposingWithoutStopAsync_StillTerminatesTheChildProcess()
    {
        // The actual property Job Objects exist to guarantee: if the
        // controlling process goes away WITHOUT a clean shutdown, the
        // kernel kills the child anyway. Simulated by disposing the
        // manager directly instead of calling StopAsync first.
        var manager = MakeManager(out _);
        await manager.StartAsync();
        int processId = manager.ProcessId!.Value;

        await manager.DisposeAsync();
        _managers.Remove(manager); // already disposed; don't double-dispose in test teardown

        Assert.False(IsProcessRunning(processId), $"Process {processId} should have been killed by its job object.");
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

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // no process with that id exists at all
        }
    }
}
