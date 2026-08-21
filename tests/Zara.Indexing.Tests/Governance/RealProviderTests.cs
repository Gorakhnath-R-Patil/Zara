using Zara.Indexing.Governance;

namespace Zara.Indexing.Tests.Governance;

/// <summary>
/// Runs each provider against the REAL OS on this actual machine — the
/// point of these is "doesn't crash and returns something sane", not
/// "produces a specific value" (which would depend on live battery/idle/
/// foreground state this test suite doesn't control). The DECISION LOGIC
/// built on top of these (<see cref="ResourceGovernor"/>) is tested
/// separately, deterministically, with injected fakes — see
/// <c>ResourceGovernorTests</c>.
/// </summary>
public class RealProviderTests
{
    [Fact]
    public void PowerStateProvider_ReturnsAPlausibleState_WithoutThrowing()
    {
        var sut = new PowerStateProvider();

        var state = sut.GetCurrentState();

        if (state.BatteryPercent is { } percent)
        {
            Assert.InRange(percent, 0, 100);
        }
    }

    [Fact]
    public void IdleTimeProvider_ReturnsANonNegativeDuration_WithoutThrowing()
    {
        var sut = new IdleTimeProvider();

        var idle = sut.GetIdleTime();

        Assert.True(idle >= TimeSpan.Zero);
        // A sanity ceiling, not a real constraint — catches a completely
        // broken tick-count calculation (e.g. an unhandled wraparound
        // producing a multi-year duration) without being a flaky assertion
        // about how long this machine has actually been idle.
        Assert.True(idle < TimeSpan.FromDays(60));
    }

    [Fact]
    public void ForegroundWindowProvider_ReturnsABoolean_WithoutThrowing()
    {
        var sut = new ForegroundWindowProvider();

        var exception = Record.Exception(() => sut.IsCurrentProcessForeground());

        Assert.Null(exception);
    }

    [Fact]
    public void BackgroundModeController_CanEnterAndExitBackgroundMode()
    {
        var sut = new BackgroundModeController();

        bool entered = sut.TryBeginBackgroundMode();
        bool exited = sut.TryEndBackgroundMode();

        Assert.True(entered, "SetPriorityClass(PROCESS_MODE_BACKGROUND_BEGIN) failed on the real current process.");
        Assert.True(exited, "SetPriorityClass(PROCESS_MODE_BACKGROUND_END) failed on the real current process.");
    }

    [Fact]
    public void BackgroundModeController_CanBeToggledRepeatedly()
    {
        var sut = new BackgroundModeController();

        for (int i = 0; i < 5; i++)
        {
            Assert.True(sut.TryBeginBackgroundMode());
            Assert.True(sut.TryEndBackgroundMode());
        }
    }
}
