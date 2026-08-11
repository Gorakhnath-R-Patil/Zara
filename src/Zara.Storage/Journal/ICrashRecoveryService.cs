namespace Zara.Storage.Journal;

/// <summary>
/// Runs once at Engine startup — ARCHITECTURE.md §19.4: finds every
/// operation still at <see cref="Zara.Core.Operations.OperationStatus.Executing"/>
/// (a crash or kill mid-operation left it there), re-checks each of its
/// still-<see cref="Zara.Core.Operations.OperationItemStatus.Pending"/> items
/// against the real filesystem to infer what actually happened, and marks
/// the operation <see cref="Zara.Core.Operations.OperationStatus.Partial"/>.
/// </summary>
/// <remarks>
/// <b>Never auto-resumes and never auto-undoes anything</b> — §19.4 is
/// explicit that a crashed operation is exactly the situation where
/// guessing is most likely to compound the damage. This only WRITES
/// per-item status based on what's observably true on disk right now;
/// deciding what to do about a partial operation (finish it, undo the
/// completed part, or leave it) is a decision surfaced to the user, not
/// made here.
/// </remarks>
public interface ICrashRecoveryService
{
    /// <summary>Returns the ids of every operation reconciled this way. An
    /// empty result on a clean startup is the expected, common case.</summary>
    Task<IReadOnlyList<Guid>> ReconcileInterruptedOperationsAsync(CancellationToken cancellationToken = default);
}
