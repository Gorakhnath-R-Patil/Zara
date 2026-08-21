namespace Zara.Indexing.Governance;

/// <inheritdoc cref="IResourceGovernor"/>
/// <remarks>
/// Pure decision logic over injected providers — deliberately not reading
/// the real Win32 APIs itself, so the RULES are unit-testable without
/// depending on this specific machine's live battery/idle/foreground state
/// at test time (which is exactly the kind of nondeterminism a test suite
/// should not depend on). <see cref="PowerStateProvider"/>/
/// <see cref="IdleTimeProvider"/>/<see cref="ForegroundWindowProvider"/> are
/// tested separately, against the real OS, for "doesn't crash and returns
/// something sane" — a different, complementary kind of test.
/// </remarks>
public sealed class ResourceGovernor : IResourceGovernor
{
    private static readonly TimeSpan TurboIdleThreshold = TimeSpan.FromMinutes(3);
    private const int LowBatteryThresholdPercent = 40;

    private readonly IPowerStateProvider _power;
    private readonly IIdleTimeProvider _idle;
    private readonly IForegroundWindowProvider _foreground;

    public ResourceGovernor(IPowerStateProvider power, IIdleTimeProvider idle, IForegroundWindowProvider foreground)
    {
        _power = power ?? throw new ArgumentNullException(nameof(power));
        _idle = idle ?? throw new ArgumentNullException(nameof(idle));
        _foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
    }

    public WorkBudget GetCurrentBudget()
    {
        var power = _power.GetCurrentState();

        // Battery rules take priority over everything else in §25.1's table —
        // checked first, unconditionally.
        if (power.OnBattery)
        {
            bool lowBattery = power.BatterySaverOn || power.BatteryPercent is null or < LowBatteryThresholdPercent;
            if (lowBattery)
            {
                return WorkBudget.PausedBudget;
            }

            // "1 thread, batch 16, no OCR, no embedding" — the OCR/embedding
            // restriction is a caller-side scoping decision (which job
            // classes even get submitted), not representable in this
            // budget's shape, so it's not encoded here; callers reading
            // Threads==1 on battery should already know to skip those job
            // classes rather than looking for a flag that isn't here.
            return new WorkBudget(Threads: 1, BatchSize: 16, IoRateMbPerSec: 15, Paused: false);
        }

        TimeSpan idleTime = _idle.GetIdleTime();
        if (idleTime >= TurboIdleThreshold)
        {
            return new WorkBudget(Threads: 4, BatchSize: 64, IoRateMbPerSec: double.PositiveInfinity, Paused: false);
        }

        bool zaraForeground = _foreground.IsCurrentProcessForeground();
        if (zaraForeground)
        {
            return new WorkBudget(Threads: 2, BatchSize: 32, IoRateMbPerSec: 40, Paused: false);
        }

        return new WorkBudget(Threads: 1, BatchSize: 16, IoRateMbPerSec: 15, Paused: false);
    }
}
