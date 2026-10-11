using Zara.Core.Files;
using Zara.Core.Operations;
using Zara.Filesystem.Shell;
using Zara.Security;

namespace Zara.Operations;

/// <summary>
/// Puts <see cref="IPolicyEngine"/> in front of <see cref="IShellOperations"/>:
/// every batch is classified before it runs, and only runs if the decision
/// allows it. This is the wiring TRACKER.md called out as missing after the
/// policy engine itself was built — a gate nothing called.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="PolicyOutcome.Block"/>: the inner executor is never
/// called and every item is reported as failed with the policy's reason.</item>
/// <item><see cref="PolicyOutcome.RequireConfirmation"/>: the
/// <see cref="IOperationConfirmer"/> decides. A "no" fails every item the
/// same way a block does.</item>
/// <item><see cref="PolicyOutcome.Allow"/>: passes straight through.</item>
/// </list>
/// Decorating the interface rather than editing <c>ShellOperations</c> keeps
/// <c>Zara.Filesystem</c> free of any dependency on <c>Zara.Security</c> and
/// covers every caller, including <see cref="UndoService"/>, without each one
/// having to remember to ask. A host that wants undo to skip re-confirmation
/// supplies a confirmer that approves it; <c>Block</c> is never skippable.
/// <para>
/// The facts handed to the classifier come from the live filesystem and are
/// best-effort: an item that cannot be inspected (gone, no access) contributes
/// nothing to the byte total rather than failing the gate, and only the
/// top-level items are checked for reparse points. Cross-volume detection
/// compares drive roots, so it does not recognise a mount point inside a
/// volume as a different volume.
/// </para>
/// </remarks>
public sealed class PolicyGatedShellOperations : IShellOperations
{
    private readonly IShellOperations _inner;
    private readonly IPolicyEngine _policy;
    private readonly IOperationConfirmer _confirmer;

    public PolicyGatedShellOperations(IShellOperations inner, IPolicyEngine policy, IOperationConfirmer confirmer)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _confirmer = confirmer ?? throw new ArgumentNullException(nameof(confirmer));
    }

    public async Task<OperationOutcome> ExecuteAsync(
        FileOperationBatch batch, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Items.Count == 0)
        {
            return await _inner.ExecuteAsync(batch, progress, cancellationToken).ConfigureAwait(false);
        }

        var context = BuildContext(batch);
        var decision = _policy.Evaluate(context);

        switch (decision.Outcome)
        {
            case PolicyOutcome.Block:
                return Refuse(batch, $"Blocked by policy: {decision.Reason ?? "operation not permitted"}");

            case PolicyOutcome.RequireConfirmation:
                bool approved = await _confirmer
                    .ConfirmAsync(new OperationConfirmationRequest(batch, decision, context), cancellationToken)
                    .ConfigureAwait(false);
                if (!approved)
                {
                    return Refuse(batch, $"Not confirmed: this operation is {decision.Risk} risk and needs approval.");
                }
                break;
        }

        return await _inner.ExecuteAsync(batch, progress, cancellationToken).ConfigureAwait(false);
    }

    private static OperationOutcome Refuse(FileOperationBatch batch, string reason) =>
        new(false, batch.Items.Select(i => new FileOperationItemOutcome(i.Source, false, null, reason)).ToList());

    private static OperationContext BuildContext(FileOperationBatch batch)
    {
        var kind = batch.Kind switch
        {
            FileOperationKind.Copy => OperationKind.Copy,
            FileOperationKind.Move => OperationKind.Move,
            FileOperationKind.Rename => OperationKind.Rename,
            FileOperationKind.Delete => OperationKind.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(batch), batch.Kind, "Unknown operation kind."),
        };

        var planItems = new List<OperationItemPlan>(batch.Items.Count);
        long totalBytes = 0;
        bool crossesVolumes = false;
        bool touchesReparsePoint = false;

        foreach (var item in batch.Items)
        {
            var destination = ResolveDestination(batch.Kind, item);
            planItems.Add(new OperationItemPlan(item.Source, destination));

            totalBytes += MeasureBytes(item.Source.Value);
            touchesReparsePoint |= IsReparsePoint(item.Source.Value);

            if (destination is { } dest && !SameVolume(item.Source.Value, dest.Value))
            {
                crossesVolumes = true;
            }
        }

        // Every operation here goes through the shell with AllowUndo, i.e. the
        // Recycle Bin; a permanent delete is not something this layer can express.
        var plan = new OperationPlan(kind, planItems, ConfirmedByUser: false);
        return new OperationContext(plan, totalBytes, crossesVolumes, touchesReparsePoint, IsPermanentDelete: false);
    }

    /// <summary>Where the item ends up, in the same canonical form the
    /// classifier compares against its blocked roots. Null for a delete.</summary>
    private static CanonicalPath? ResolveDestination(FileOperationKind kind, FileOperationItem item)
    {
        string name = item.NewName ?? Path.GetFileName(item.Source.Value.TrimEnd('\\'));

        switch (kind)
        {
            case FileOperationKind.Delete:
                return null;

            case FileOperationKind.Rename:
                string? folder = Path.GetDirectoryName(item.Source.Value.TrimEnd('\\'));
                return folder is null || string.IsNullOrEmpty(item.NewName)
                    ? item.Source
                    : CanonicalPath.FromCanonicalizedString(Path.Combine(folder, item.NewName));

            default:
                return item.DestinationFolder is { IsDefault: false } destinationFolder
                    ? CanonicalPath.FromCanonicalizedString(Path.Combine(destinationFolder.Value.TrimEnd('\\'), name))
                    : null;
        }
    }

    private static long MeasureBytes(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (!Directory.Exists(path))
            {
                return 0;
            }

            long total = 0;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint, // never follow a link out of the tree
            };
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                total += file.Length;
            }
            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
}
