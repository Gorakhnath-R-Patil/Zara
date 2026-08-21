using Zara.Indexing.Governance;

namespace Zara.Indexing.Tests.Governance;

public class ResourceGovernorTests
{
    private sealed class FakePower(PowerState state) : IPowerStateProvider
    {
        public PowerState GetCurrentState() => state;
    }

    private sealed class FakeIdle(TimeSpan idle) : IIdleTimeProvider
    {
        public TimeSpan GetIdleTime() => idle;
    }

    private sealed class FakeForeground(bool isForeground) : IForegroundWindowProvider
    {
        public bool IsCurrentProcessForeground() => isForeground;
    }

    private static ResourceGovernor Make(PowerState power, TimeSpan idle, bool zaraForeground) =>
        new(new FakePower(power), new FakeIdle(idle), new FakeForeground(zaraForeground));

    private static readonly PowerState OnAc = new(OnBattery: false, BatteryPercent: 100, BatterySaverOn: false);

    [Fact]
    public void OnBattery_BelowThreshold_IsPaused()
    {
        var power = new PowerState(OnBattery: true, BatteryPercent: 39, BatterySaverOn: false);
        var sut = Make(power, TimeSpan.Zero, zaraForeground: true);

        var budget = sut.GetCurrentBudget();

        Assert.True(budget.Paused);
        Assert.Equal(0, budget.Threads);
    }

    [Fact]
    public void OnBattery_AtThreshold_IsNotPaused()
    {
        // ">40%" per §25.1's table — exactly 40 is the "still OK" boundary.
        var power = new PowerState(OnBattery: true, BatteryPercent: 40, BatterySaverOn: false);
        var sut = Make(power, TimeSpan.Zero, zaraForeground: true);

        var budget = sut.GetCurrentBudget();

        Assert.False(budget.Paused);
        Assert.Equal(1, budget.Threads);
    }

    [Fact]
    public void OnBattery_BatterySaverOn_IsPaused_RegardlessOfPercent()
    {
        var power = new PowerState(OnBattery: true, BatteryPercent: 90, BatterySaverOn: true);
        var sut = Make(power, TimeSpan.Zero, zaraForeground: true);

        var budget = sut.GetCurrentBudget();

        Assert.True(budget.Paused);
    }

    [Fact]
    public void OnBattery_UnknownPercent_IsPaused_ConservativeDefault()
    {
        var power = new PowerState(OnBattery: true, BatteryPercent: null, BatterySaverOn: false);
        var sut = Make(power, TimeSpan.Zero, zaraForeground: true);

        var budget = sut.GetCurrentBudget();

        Assert.True(budget.Paused);
    }

    [Fact]
    public void OnBattery_HealthyLevel_GetsASingleThrottledThread()
    {
        var power = new PowerState(OnBattery: true, BatteryPercent: 80, BatterySaverOn: false);
        var sut = Make(power, idle: TimeSpan.FromHours(1), zaraForeground: false); // even long-idle/background doesn't override battery rules

        var budget = sut.GetCurrentBudget();

        Assert.False(budget.Paused);
        Assert.Equal(1, budget.Threads);
        Assert.Equal(16, budget.BatchSize);
    }

    [Fact]
    public void OnAc_LongIdle_GetsTurboBudget()
    {
        var sut = Make(OnAc, idle: TimeSpan.FromMinutes(5), zaraForeground: false);

        var budget = sut.GetCurrentBudget();

        Assert.Equal(4, budget.Threads);
        Assert.Equal(64, budget.BatchSize);
        Assert.Equal(double.PositiveInfinity, budget.IoRateMbPerSec);
        Assert.False(budget.Paused);
    }

    [Fact]
    public void OnAc_JustUnderIdleThreshold_DoesNotGetTurbo()
    {
        var sut = Make(OnAc, idle: TimeSpan.FromMinutes(3) - TimeSpan.FromSeconds(1), zaraForeground: false);

        var budget = sut.GetCurrentBudget();

        Assert.NotEqual(4, budget.Threads);
    }

    [Fact]
    public void OnAc_ActiveAndZaraForeground_GetsModerateBudget()
    {
        var sut = Make(OnAc, idle: TimeSpan.Zero, zaraForeground: true);

        var budget = sut.GetCurrentBudget();

        Assert.Equal(2, budget.Threads);
        Assert.Equal(32, budget.BatchSize);
        Assert.Equal(40, budget.IoRateMbPerSec);
    }

    [Fact]
    public void OnAc_ActiveAndOtherAppForeground_GetsMinimalBudget()
    {
        var sut = Make(OnAc, idle: TimeSpan.Zero, zaraForeground: false);

        var budget = sut.GetCurrentBudget();

        Assert.Equal(1, budget.Threads);
        Assert.Equal(16, budget.BatchSize);
        Assert.Equal(15, budget.IoRateMbPerSec);
    }

    [Fact]
    public void NeverPaused_WhenOnAc_RegardlessOfForegroundOrIdle()
    {
        // Only battery rules pause the indexer in this v1's scope.
        Assert.False(Make(OnAc, TimeSpan.Zero, true).GetCurrentBudget().Paused);
        Assert.False(Make(OnAc, TimeSpan.Zero, false).GetCurrentBudget().Paused);
        Assert.False(Make(OnAc, TimeSpan.FromHours(1), false).GetCurrentBudget().Paused);
    }
}
