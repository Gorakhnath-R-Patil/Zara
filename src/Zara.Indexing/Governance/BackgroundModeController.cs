using Zara.Indexing.Governance.Interop;

namespace Zara.Indexing.Governance;

/// <inheritdoc cref="IBackgroundModeController"/>
public sealed class BackgroundModeController : IBackgroundModeController
{
    public bool TryBeginBackgroundMode() =>
        NativeMethods.SetPriorityClass(GetCurrentProcessPseudoHandle(), NativeMethods.ProcessModeBackgroundBegin);

    public bool TryEndBackgroundMode() =>
        NativeMethods.SetPriorityClass(GetCurrentProcessPseudoHandle(), NativeMethods.ProcessModeBackgroundEnd);

    // The well-known GetCurrentProcess() pseudo-handle (-1) — valid for
    // SetPriorityClass without needing to open/track a real handle via
    // System.Diagnostics.Process.
    private static nint GetCurrentProcessPseudoHandle() => -1;
}
