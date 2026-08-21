using Zara.Indexing.Governance.Interop;

namespace Zara.Indexing.Governance;

/// <inheritdoc cref="IForegroundWindowProvider"/>
public sealed class ForegroundWindowProvider : IForegroundWindowProvider
{
    public bool IsCurrentProcessForeground()
    {
        nint foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow == 0)
        {
            return false; // no window has focus (e.g. the desktop itself)
        }

        NativeMethods.GetWindowThreadProcessId(foregroundWindow, out uint ownerProcessId);
        return ownerProcessId == (uint)Environment.ProcessId;
    }
}
