using System.Runtime.InteropServices;

namespace Zara.Indexing.Governance.Interop;

/// <summary>
/// Hand-rolled <c>DllImport</c> for T48's Win32 surface — flat kernel32/
/// user32 exports with stable, well-documented signatures, matching this
/// codebase's established rule (Zara.Filesystem's <c>NativeMethods</c>,
/// Zara.EngineClient's Job Object interop): plain functions like these
/// don't carry the vtable-ordering risk that justified pulling in a vetted
/// wrapper library for <c>IFileOperation</c> (T31).
/// </summary>
internal static class NativeMethods
{
    // ── GetSystemPowerStatus ─────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;        // 0 = offline (on battery), 1 = online (AC), 255 = unknown
        public byte BatteryFlag;         // bit 7 (128) = no system battery
        public byte BatteryLifePercent;  // 0-100, 255 = unknown
        public byte SystemStatusFlag;    // bit 0 = Battery Saver is on (Windows 10+)
        public uint BatteryLifeTime;     // seconds remaining, 0xFFFFFFFF = unknown
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    internal const byte NoSystemBatteryFlag = 128;
    internal const byte BatterySaverOnFlag = 1;

    // ── GetLastInputInfo ─────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    // ── GetForegroundWindow / GetWindowThreadProcessId ──────────────────────
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    // ── SetPriorityClass (PROCESS_MODE_BACKGROUND_*) ────────────────────────
    internal const uint ProcessModeBackgroundBegin = 0x00100000;
    internal const uint ProcessModeBackgroundEnd = 0x00200000;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetPriorityClass(nint hProcess, uint dwPriorityClass);
}
