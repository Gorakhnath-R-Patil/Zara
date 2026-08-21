namespace Zara.Indexing.Governance;

/// <param name="OnBattery">False when on AC power or the machine has no
/// battery at all (a desktop) — both cases behave the same for throttling
/// purposes.</param>
/// <param name="BatteryPercent">0-100, or null when the OS reports "unknown".</param>
/// <param name="BatterySaverOn">Windows' own Battery Saver mode — §25.1's
/// table treats this the same as "below 40%" (pause background indexing).</param>
public sealed record PowerState(bool OnBattery, int? BatteryPercent, bool BatterySaverOn);

public interface IPowerStateProvider
{
    PowerState GetCurrentState();
}
