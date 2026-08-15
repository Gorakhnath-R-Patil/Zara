namespace Zara.EngineClient.Process;

/// <summary>
/// The exact reconnect schedule from ARCHITECTURE.md §9.4: "Restart with
/// backoff: 1s, 4s, 16s, 60s. 3 crashes in 5 min → stop auto-restart,
/// surface a diagnostics link." Pure and deterministic — takes "now" as a
/// parameter rather than reading the clock itself, so tests don't need to
/// sleep in real time to exercise the 5-minute window.
/// </summary>
public sealed class ReconnectBackoff
{
    private static readonly TimeSpan[] Delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(60)];
    private static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(5);
    private const int MaxCrashesInWindow = 3;

    private readonly List<DateTimeOffset> _recentCrashes = [];
    private int _attempt;

    /// <summary>True once 3 crashes have landed within the trailing 5-minute
    /// window — the caller should stop auto-restarting and surface
    /// diagnostics instead.</summary>
    public bool ShouldGiveUp(DateTimeOffset now)
    {
        PruneOldCrashes(now);
        return _recentCrashes.Count >= MaxCrashesInWindow;
    }

    /// <summary>Records a crash and returns how long to wait before the next
    /// restart attempt. Call <see cref="ShouldGiveUp"/> first — this does not
    /// check the give-up condition itself.</summary>
    public TimeSpan RecordCrashAndGetNextDelay(DateTimeOffset now)
    {
        PruneOldCrashes(now);
        _recentCrashes.Add(now);

        TimeSpan delay = Delays[Math.Min(_attempt, Delays.Length - 1)];
        _attempt++;
        return delay;
    }

    /// <summary>Call after a successful reconnect — a healthy run resets the
    /// backoff schedule back to the beginning (a crash right after a long
    /// healthy period shouldn't inherit a slow delay from a much earlier streak).</summary>
    public void Reset() => _attempt = 0;

    private void PruneOldCrashes(DateTimeOffset now) => _recentCrashes.RemoveAll(t => now - t > CrashWindow);
}
