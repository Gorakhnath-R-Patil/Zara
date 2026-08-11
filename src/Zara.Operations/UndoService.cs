using Zara.Core.Files;
using Zara.Core.Operations;
using Zara.Filesystem.Hashing;
using Zara.Filesystem.Shell;

namespace Zara.Operations;

/// <inheritdoc cref="IUndoService"/>
public sealed class UndoService : IUndoService
{
    private readonly IOperationJournal _journal;
    private readonly IShellOperations _shellOperations;
    private readonly IContentHasher _contentHasher;

    public UndoService(IOperationJournal journal, IShellOperations shellOperations, IContentHasher contentHasher)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _shellOperations = shellOperations ?? throw new ArgumentNullException(nameof(shellOperations));
        _contentHasher = contentHasher ?? throw new ArgumentNullException(nameof(contentHasher));
    }

    public async Task<UndoResult> UndoAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var operation = await _journal.GetAsync(operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No operation found with id {operationId}.");

        if (operation.Kind == OperationKind.Delete)
        {
            // Deliberately unsupported — see IUndoService's remarks.
            return new UndoResult(false,
                ["Undoing a delete requires restoring from the Recycle Bin, which this version does not implement."]);
        }

        var items = await _journal.GetItemsAsync(operationId, cancellationToken).ConfigureAwait(false);
        var completedItems = items.Where(i => i.Status == OperationItemStatus.Completed).ToList();

        if (completedItems.Count == 0)
        {
            return new UndoResult(true, []); // nothing landed, so there's nothing to reverse
        }

        var unrecoverable = new List<string>();
        var undoBatch = operation.Kind switch
        {
            OperationKind.Move or OperationKind.Rename => BuildMoveBackBatch(completedItems, unrecoverable),
            OperationKind.Copy => BuildDeleteCopiesBatch(completedItems, unrecoverable),
            _ => new FileOperationBatch(FileOperationKind.Move, []),
        };

        if (undoBatch.Items.Count == 0)
        {
            return new UndoResult(unrecoverable.Count == 0, unrecoverable);
        }

        // The undo is itself journaled as a new operation (kind=Undo) —
        // §19.5: "Undo is itself an operation, journaled... Undo is
        // therefore redoable."
        var undoPlan = new OperationPlan(
            OperationKind.Undo,
            undoBatch.Items.Select(i => new OperationItemPlan(i.Source, i.DestinationFolder)).ToList(),
            RiskClass: operation.RiskClass,
            ConfirmedByUser: true,
            UserRequest: $"undo of operation {operationId}");
        var undoOperationId = await _journal.BeginAsync(undoPlan, cancellationToken).ConfigureAwait(false);
        await _journal.MarkExecutingAsync(undoOperationId, cancellationToken).ConfigureAwait(false);

        var outcome = await _shellOperations.ExecuteAsync(undoBatch, cancellationToken: cancellationToken).ConfigureAwait(false);

        for (int i = 0; i < outcome.Items.Count; i++)
        {
            var itemOutcome = outcome.Items[i];
            await _journal.RecordItemAsync(
                undoOperationId,
                new OperationItemOutcome(
                    i,
                    itemOutcome.Succeeded ? OperationItemStatus.Completed : OperationItemStatus.Failed,
                    itemOutcome.ResultPath,
                    itemOutcome.Succeeded ? null : itemOutcome.ErrorMessage),
                cancellationToken).ConfigureAwait(false);
        }

        await _journal.CompleteAsync(
            undoOperationId, outcome.Succeeded ? OperationStatus.Completed : OperationStatus.Partial, cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Succeeded && unrecoverable.Count == 0)
        {
            await _journal.CompleteAsync(operationId, OperationStatus.Undone, cancellationToken).ConfigureAwait(false);
        }

        return new UndoResult(outcome.Succeeded && unrecoverable.Count == 0, unrecoverable);
    }

    /// <summary>Undoing a Move or a Rename is the same shape: move the item
    /// currently at its destination back to its original folder, under its
    /// original name. (A Rename is a Move whose destination folder happens
    /// to equal the source folder — the shell operation is identical either way.)</summary>
    private static FileOperationBatch BuildMoveBackBatch(List<OperationItemRecord> completedItems, List<string> unrecoverable)
    {
        var reverseItems = new List<FileOperationItem>();

        foreach (var item in completedItems)
        {
            if (item.DestPath is null)
            {
                unrecoverable.Add($"Item {item.Seq}: no recorded destination, cannot undo.");
                continue;
            }

            string? originalFolder = Path.GetDirectoryName(item.SourcePath);
            string? originalName = Path.GetFileName(item.SourcePath);

            if (originalFolder is null || string.IsNullOrEmpty(originalName))
            {
                unrecoverable.Add($"Item {item.Seq}: cannot determine the original location.");
                continue;
            }

            reverseItems.Add(new FileOperationItem(
                CanonicalPath.FromCanonicalizedString(item.DestPath),
                CanonicalPath.FromCanonicalizedString(originalFolder),
                originalName));
        }

        return new FileOperationBatch(FileOperationKind.Move, reverseItems);
    }

    /// <summary>Undoing a Copy deletes the copies — but only after verifying
    /// (via content hash, §19.1) that the copy still matches its original.
    /// A copy that's been modified since, or whose original is now gone in
    /// a way we can't verify against, is left alone rather than risk
    /// deleting the wrong thing.</summary>
    private FileOperationBatch BuildDeleteCopiesBatch(List<OperationItemRecord> completedItems, List<string> unrecoverable)
    {
        var reverseItems = new List<FileOperationItem>();

        foreach (var item in completedItems)
        {
            if (item.DestPath is null)
            {
                unrecoverable.Add($"Item {item.Seq}: no recorded destination, cannot undo.");
                continue;
            }

            if (!File.Exists(item.DestPath))
            {
                unrecoverable.Add($"Item {item.Seq}: the copy no longer exists.");
                continue;
            }

            if (File.Exists(item.SourcePath))
            {
                string originalHash = _contentHasher.ComputeHex(CanonicalPath.FromCanonicalizedString(item.SourcePath));
                string copyHash = _contentHasher.ComputeHex(CanonicalPath.FromCanonicalizedString(item.DestPath));

                if (!string.Equals(originalHash, copyHash, StringComparison.Ordinal))
                {
                    unrecoverable.Add($"Item {item.Seq}: the copy no longer matches the original — not deleting it.");
                    continue;
                }
            }
            // else: the original itself is gone (independently moved or
            // deleted since) — can't verify, but it's still legitimately
            // "our copy" per the journal, so it's still deleted. Revisit if
            // this proves too permissive in practice.

            reverseItems.Add(new FileOperationItem(CanonicalPath.FromCanonicalizedString(item.DestPath)));
        }

        return new FileOperationBatch(FileOperationKind.Delete, reverseItems);
    }
}
