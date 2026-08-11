using Zara.Core.Operations;

namespace Zara.Storage.Journal;

/// <inheritdoc cref="ICrashRecoveryService"/>
public sealed class CrashRecoveryService : ICrashRecoveryService
{
    private readonly IOperationJournal _journal;

    public CrashRecoveryService(IOperationJournal journal)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

    public async Task<IReadOnlyList<Guid>> ReconcileInterruptedOperationsAsync(CancellationToken cancellationToken = default)
    {
        var interrupted = await _journal.GetInterruptedAsync(cancellationToken).ConfigureAwait(false);
        var reconciled = new List<Guid>();

        foreach (var operation in interrupted)
        {
            var items = await _journal.GetItemsAsync(operation.Id, cancellationToken).ConfigureAwait(false);

            foreach (var item in items)
            {
                if (item.Status != OperationItemStatus.Pending)
                {
                    continue; // already resolved before the crash — leave it alone
                }

                var inferred = InferStatus(operation.Kind, item);
                await _journal.RecordItemAsync(operation.Id, new OperationItemOutcome(item.Seq, inferred), cancellationToken)
                    .ConfigureAwait(false);
            }

            // §19.4: always 'partial', even if reconciliation happens to
            // find every item completed — "partial" here means "this
            // finished without journal supervision", worth a user's
            // attention once, not silently promoted back to 'completed'.
            await _journal.CompleteAsync(operation.Id, OperationStatus.Partial, cancellationToken).ConfigureAwait(false);
            reconciled.Add(operation.Id);
        }

        return reconciled;
    }

    /// <summary>ARCHITECTURE.md §19.4's exact inference rules, applied
    /// per operation kind (a "destination" means something different for
    /// each): "source gone + dest present = completed; source present +
    /// dest absent = pending; both present = ambiguous" — for Move/Rename.
    /// Copy never removes its source, so only the destination's presence is
    /// meaningful there. Delete has no destination at all.</summary>
    private static OperationItemStatus InferStatus(OperationKind kind, OperationItemRecord item)
    {
        bool sourceExists = PathExists(item.SourcePath);
        bool destExists = PathExists(item.DestPath);

        return kind switch
        {
            OperationKind.Delete => sourceExists ? OperationItemStatus.Pending : OperationItemStatus.Completed,
            OperationKind.Copy => destExists ? OperationItemStatus.Completed : OperationItemStatus.Pending,
            OperationKind.Move or OperationKind.Rename => (sourceExists, destExists) switch
            {
                (false, true) => OperationItemStatus.Completed,
                (true, false) => OperationItemStatus.Pending,
                _ => OperationItemStatus.Unrecoverable, // both present or both absent — can't safely infer
            },
            _ => OperationItemStatus.Unrecoverable,
        };
    }

    private static bool PathExists(string? path) => path is not null && (File.Exists(path) || Directory.Exists(path));
}
