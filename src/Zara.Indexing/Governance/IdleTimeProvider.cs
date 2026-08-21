using Zara.Indexing.Governance.Interop;

namespace Zara.Indexing.Governance;

/// <inheritdoc cref="IIdleTimeProvider"/>
public sealed class IdleTimeProvider : IIdleTimeProvider
{
    public TimeSpan GetIdleTime()
    {
        var info = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LASTINPUTINFO>(),
        };

        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero; // conservative: "just active" rather than claiming a long idle we can't confirm
        }

        // Both GetLastInputInfo's dwTime and Environment.TickCount are backed
        // by the same wrapping 32-bit tick counter, so an unchecked int
        // subtraction is correct across a wraparound (~49.7 days uptime) the
        // same way it would be with GetTickCount64 on a system that's been up
        // longer than that — the difference is only ever wrong if idle time
        // itself exceeds the wrap period, which is not a real scenario.
        int elapsedMs = unchecked(Environment.TickCount - (int)info.dwTime);
        return TimeSpan.FromMilliseconds(Math.Max(0, elapsedMs));
    }
}
