using SysProcess = System.Diagnostics.Process;

namespace Zara.EngineClient.Process;

/// <inheritdoc cref="IEngineProcessManager"/>
public sealed class EngineProcessManager : IEngineProcessManager
{
    private readonly string _engineExecutablePath;
    private readonly string? _arguments;
    private readonly EngineJobObject _jobObject = new();
    private SysProcess? _process;
    private bool _stopping;

    /// <param name="arguments">Raw command-line arguments passed to the
    /// Engine process as-is — e.g. <c>--pipe-name=... --db-path=...</c> to
    /// spawn an isolated instance (tests; a future multi-profile scenario),
    /// left null for the real per-user defaults.</param>
    public EngineProcessManager(string engineExecutablePath, string? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineExecutablePath);
        _engineExecutablePath = engineExecutablePath;
        _arguments = arguments;
    }

    public bool IsRunning => _process is { HasExited: false };

    public int? ProcessId => _process?.Id;

    public event EventHandler<EngineProcessExitedEventArgs>? ProcessExited;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _stopping = false;

        var startInfo = new System.Diagnostics.ProcessStartInfo(_engineExecutablePath)
        {
            Arguments = _arguments ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _process = SysProcess.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{_engineExecutablePath}'.");
        _process.EnableRaisingEvents = true;
        _process.Exited += OnProcessExited;

        // Not fatal if this fails (the process is running regardless) —
        // kill-on-close simply won't apply. A future logging pass should
        // surface that, but it doesn't block the primary goal of getting
        // the Engine running.
        _jobObject.Assign(_process.SafeHandle);

        return Task.CompletedTask;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_stopping)
        {
            return; // a deliberate StopAsync, not a crash — nothing to report
        }

        int exitCode = -1;
        try
        {
            exitCode = _process?.ExitCode ?? -1;
        }
        catch (InvalidOperationException)
        {
            // Exit code unavailable in some teardown races — still report the exit itself.
        }

        ProcessExited?.Invoke(this, new EngineProcessExitedEventArgs(exitCode));
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _stopping = true;

        if (_process is { HasExited: false } process)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _process?.Dispose();
        _jobObject.Dispose();
    }
}
