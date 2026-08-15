using Grpc.Core;
using Zara.Contracts.Journal;
using Zara.Core.Operations;

namespace Zara.Engine.Services;

public sealed class JournalServiceImpl : JournalService.JournalServiceBase
{
    private readonly IOperationJournal _journal;

    public JournalServiceImpl(IOperationJournal journal)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

    public override async Task GetUndoable(
        GetUndoableRequest request, IServerStreamWriter<OperationSummary> responseStream, ServerCallContext context)
    {
        int limit = request.Limit > 0 ? request.Limit : 50;
        var operations = await _journal.GetUndoableAsync(limit, context.CancellationToken).ConfigureAwait(false);

        foreach (var operation in operations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            await responseStream.WriteAsync(new OperationSummary
            {
                Id = operation.Id.ToString(),
                Kind = operation.Kind.ToString(),
                Status = operation.Status.ToString(),
                CreatedUtc = operation.CreatedUtc,
                ItemCount = operation.ItemCount,
            }).ConfigureAwait(false);
        }
    }
}
