using Zara.Filesystem.Shell;
using Zara.Security;

namespace Zara.Operations;

/// <summary>What the user (or a stand-in for the user) is being asked to
/// approve: the exact batch about to run and why the policy engine wants a
/// human decision first.</summary>
public sealed record OperationConfirmationRequest(
    FileOperationBatch Batch, PolicyDecision Decision, OperationContext Context);

/// <summary>
/// The seam between the policy gate and whatever can actually ask a person.
/// ARCHITECTURE.md §17.2 requires operations above LOW risk to be confirmed;
/// this project has no UI yet, so the gate only knows it needs a yes/no and
/// leaves who answers to the composition root.
/// </summary>
public interface IOperationConfirmer
{
    Task<bool> ConfirmAsync(OperationConfirmationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Refuses every confirmation. The safe default for any host that
/// has not wired up a real prompt: an operation that needs a human is not
/// run, rather than assumed approved.</summary>
public sealed class DenyingOperationConfirmer : IOperationConfirmer
{
    public Task<bool> ConfirmAsync(OperationConfirmationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
