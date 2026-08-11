using Vanara.Windows.Shell;
using Zara.Core.Files;

namespace Zara.Filesystem.Shell;

/// <inheritdoc cref="IShellOperations"/>
public sealed class ShellOperations : IShellOperations
{
    public Task<OperationOutcome> ExecuteAsync(
        FileOperationBatch batch, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        // IFileOperation requires an STA thread — Vanara enforces this
        // itself (OleThreadState.EnsureSTA(), confirmed by running this
        // against Task.Run: it throws ThreadStateException on a .NET
        // ThreadPool thread, which is MTA by default). A dedicated STA
        // thread per call is simple and correct; a persistent, reused
        // STA message-pump thread would be the throughput optimization if
        // this ever becomes a hot path, which file operation batches are not.
        var completion = new TaskCompletionSource<OperationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Execute(batch, progress, cancellationToken));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }

    private static OperationOutcome Execute(FileOperationBatch batch, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var outcomes = new List<FileOperationItemOutcome>();
        int completed = 0;

        using var op = new ShellFileOperations
        {
            // NoConfirmation + Silent + NoErrorUI: Zara owns confirmation
            // (ARCHITECTURE.md §21.3's preview dialog, a UI-layer concern) —
            // the shell must never pop its own dialog on top of that.
            // AllowUndo is what makes delete land in the Recycle Bin instead
            // of being permanent (§17.2: permanent delete is never exposed
            // to anything but an explicit, typed user confirmation).
            Options = ShellFileOperations.OperationFlags.NoConfirmation
                    | ShellFileOperations.OperationFlags.Silent
                    | ShellFileOperations.OperationFlags.NoErrorUI
                    | ShellFileOperations.OperationFlags.AllowUndo,
        };

        void RecordOutcome(object? sender, ShellFileOperations.ShellFileOpEventArgs e)
        {
            completed++;
            bool succeeded = e.Result.Succeeded;

            outcomes.Add(new FileOperationItemOutcome(
                TryCanonicalize(e.SourceItem?.FileSystemPath) ?? default,
                succeeded,
                succeeded ? TryCanonicalize(e.DestItem?.FileSystemPath) : null,
                succeeded ? null : e.Result.ToString()));

            progress?.Report(new OperationProgress(completed, batch.Items.Count, e.Name));
        }

        op.PostCopyItem += RecordOutcome;
        op.PostMoveItem += RecordOutcome;
        op.PostDeleteItem += RecordOutcome;
        op.PostRenameItem += RecordOutcome;

        foreach (var item in batch.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (batch.Kind)
            {
                case FileOperationKind.Copy:
                    op.QueueCopyOperation(new ShellItem(StripExtendedPrefix(item.Source.Value)), new ShellFolder(StripExtendedPrefix(item.DestinationFolder!.Value.Value)), item.NewName);
                    break;
                case FileOperationKind.Move:
                    op.QueueMoveOperation(new ShellItem(StripExtendedPrefix(item.Source.Value)), new ShellFolder(StripExtendedPrefix(item.DestinationFolder!.Value.Value)), item.NewName);
                    break;
                case FileOperationKind.Rename:
                    op.QueueRenameOperation(new ShellItem(StripExtendedPrefix(item.Source.Value)), item.NewName!);
                    break;
                case FileOperationKind.Delete:
                    op.QueueDeleteOperation(new ShellItem(StripExtendedPrefix(item.Source.Value)));
                    break;
                default:
                    throw new NotSupportedException($"Unhandled {nameof(FileOperationKind)}: {batch.Kind}");
            }
        }

        op.PerformOperations();

        return new OperationOutcome(outcomes.Count > 0 && outcomes.All(o => o.Succeeded), outcomes);
    }

    /// <summary>
    /// The Shell namespace API (what <c>ShellItem</c>/<c>ShellFolder</c> use
    /// under the hood — <c>SHCreateItemFromParsingName</c>) does not
    /// understand the <c>\\?\</c> extended-length-path syntax every
    /// <see cref="CanonicalPath"/> in this codebase carries (§10.1) — confirmed
    /// directly: <c>ShellItem</c>'s constructor throws <c>ArgumentException</c>
    /// ("Value does not fall within the expected range") on one. Shell32 has
    /// its own path parsing rules, distinct from raw Win32 file I/O's.
    /// </summary>
    private static string StripExtendedPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }

        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    private static CanonicalPath? TryCanonicalize(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            string prefixed = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path;
            return CanonicalPath.FromCanonicalizedString(prefixed);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
