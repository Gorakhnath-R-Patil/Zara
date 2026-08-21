using Zara.Indexing.Governance.Interop;

namespace Zara.Indexing.Governance;

/// <inheritdoc cref="IPowerStateProvider"/>
public sealed class PowerStateProvider : IPowerStateProvider
{
    public PowerState GetCurrentState()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var status))
        {
            // Matches the OS's own "unknown" convention rather than
            // guessing — a caller that can't tell should throttle
            // conservatively, not assume the best case.
            return new PowerState(OnBattery: true, BatteryPercent: null, BatterySaverOn: false);
        }

        bool hasBattery = (status.BatteryFlag & NativeMethods.NoSystemBatteryFlag) == 0;
        bool onBattery = hasBattery && status.ACLineStatus == 0;
        int? percent = status.BatteryLifePercent == 255 ? null : status.BatteryLifePercent;
        bool batterySaverOn = (status.SystemStatusFlag & NativeMethods.BatterySaverOnFlag) != 0;

        return new PowerState(onBattery, percent, batterySaverOn);
    }
}
