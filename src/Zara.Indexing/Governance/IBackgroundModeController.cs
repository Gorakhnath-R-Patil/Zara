namespace Zara.Indexing.Governance;

/// <summary>
/// Wraps <c>PROCESS_MODE_BACKGROUND_BEGIN</c>/<c>_END</c> — ARCHITECTURE.md
/// §25.1: "the single most effective call — it lowers both CPU *and* I/O
/// priority OS-wide" for the current process, without fighting the Windows
/// scheduler the way a hard CPU affinity mask would.
/// </summary>
public interface IBackgroundModeController
{
    /// <summary>Enters background mode. Returns false if the OS refused
    /// (e.g. already in background mode) rather than throwing — this is a
    /// best-effort priority hint, not a correctness-critical operation.</summary>
    bool TryBeginBackgroundMode();

    bool TryEndBackgroundMode();
}
