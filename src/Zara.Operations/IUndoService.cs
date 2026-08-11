namespace Zara.Operations;

public sealed record UndoResult(bool Succeeded, IReadOnlyList<string> UnrecoverableReasons);

/// <summary>
/// Builds and executes the reverse of a completed operation —
/// ARCHITECTURE.md §19.1's reversibility table and §19.5's undo semantics.
/// </summary>
/// <remarks>
/// <b>Delete is not undoable by this implementation.</b> §19.1 marks
/// Recycle-Bin delete as "fully reversible" via restoring the
/// <c>IShellItem</c> the Recycle Bin tracked — but implementing that restore
/// correctly (finding the right Recycle Bin entry, handling it having been
/// purged or the Bin emptied) is real, separate work this milestone doesn't
/// include. <see cref="IUndoService.UndoAsync"/> returns a clear, honest
/// failure for a Delete operation rather than a silently-wrong attempt.
/// Move/Rename/Copy undo ARE fully implemented and verified byte-identical
/// (see the property tests in <c>Zara.Operations.Tests</c>).
/// </remarks>
public interface IUndoService
{
    Task<UndoResult> UndoAsync(Guid operationId, CancellationToken cancellationToken = default);
}
