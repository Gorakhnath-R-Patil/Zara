namespace Zara.Core.Operations;

/// <summary>
/// The durable record of every file operation Zara performs —
/// ARCHITECTURE.md §19.2/§19.3. The write protocol is fixed: 1) BeginAsync
/// durably records the full plan BEFORE anything executes; 2) the caller
/// performs the actual filesystem work; 3) RecordItemAsync logs each item's
/// real outcome as it happens; 4) CompleteAsync closes out the operation.
/// A process that dies between steps 1 and 4 leaves a row at
/// <see cref="OperationStatus.Executing"/> that <see cref="GetInterruptedAsync"/>
/// finds on the next startup — that's the whole crash-recovery story (§19.4).
/// </summary>
public interface IOperationJournal
{
    /// <summary>Durably records the plan and returns its new operation id.
    /// Must complete before the caller performs any actual file operation.</summary>
    Task<Guid> BeginAsync(OperationPlan plan, CancellationToken cancellationToken = default);

    /// <summary>Marks the operation as actively executing — called once,
    /// right before the first file operation begins.</summary>
    Task MarkExecutingAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task RecordItemAsync(Guid operationId, OperationItemOutcome outcome, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid operationId, OperationStatus status, CancellationToken cancellationToken = default);

    Task<OperationRecord?> GetAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationItemRecord>> GetItemsAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>Operations still at <see cref="OperationStatus.Executing"/> —
    /// the crash-recovery entry point (§19.4). An empty result on a clean
    /// startup is the expected, common case.</summary>
    Task<IReadOnlyList<OperationRecord>> GetInterruptedAsync(CancellationToken cancellationToken = default);

    /// <summary>Completed, not-yet-undone, not-yet-expired operations, most
    /// recent first — the undo stack's backing store (§19.5).</summary>
    Task<IReadOnlyList<OperationRecord>> GetUndoableAsync(int limit, CancellationToken cancellationToken = default);
}
