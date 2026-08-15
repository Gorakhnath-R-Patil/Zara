namespace Zara.EngineClient.Process;

public sealed record EngineProcessExitedEventArgs(int ExitCode);

/// <summary>
/// Spawns and supervises the Engine child process —
/// ARCHITECTURE.md §9.4's lifecycle: "spawn Zara.Engine as a child (job
/// object: kill-on-close by default)".
/// </summary>
public interface IEngineProcessManager : IAsyncDisposable
{
    bool IsRunning { get; }

    /// <summary>The spawned process's id, or null if it hasn't been started.
    /// Useful for diagnostics/logging — not needed for normal operation.</summary>
    int? ProcessId { get; }

    /// <summary>Raised when the Engine process exits on its own (crash or
    /// otherwise) — NOT raised for a deliberate <see cref="StopAsync"/>.</summary>
    event EventHandler<EngineProcessExitedEventArgs>? ProcessExited;

    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>A deliberate, clean shutdown — does not raise <see cref="ProcessExited"/>.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
