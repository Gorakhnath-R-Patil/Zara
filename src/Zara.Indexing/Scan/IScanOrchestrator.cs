using Zara.Core.Files;

namespace Zara.Indexing.Scan;

/// <summary>
/// Drives a full scan of <paramref name="root"/>: enumerates its direct
/// children, indexes files immediately, and walks each not-yet-completed
/// subdirectory fully, checkpointing after each one finishes. A scan
/// interrupted (crash, kill) and restarted with the same
/// <c>(volumeId, root)</c> skips whatever subdirectories were already
/// checkpointed complete — see ARCHITECTURE.md §11.3 and
/// <c>Migrations/002_scan_checkpoints.sql</c>.
/// </summary>
public interface IScanOrchestrator
{
    Task<ScanResult> ScanAsync(
        long volumeId, CanonicalPath root, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}
