using Grpc.Core;
using Zara.Contracts.Search;
using Zara.Search.Names;

namespace Zara.Engine.Services;

public sealed class SearchServiceImpl : SearchService.SearchServiceBase
{
    private readonly INameIndex _nameIndex;

    public SearchServiceImpl(INameIndex nameIndex)
    {
        _nameIndex = nameIndex ?? throw new ArgumentNullException(nameof(nameIndex));
    }

    public override async Task Search(SearchRequest request, IServerStreamWriter<SearchHit> responseStream, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return;
        }

        int maxResults = request.MaxResults > 0 ? request.MaxResults : 50;
        var hits = _nameIndex.Search(request.Query, maxResults);

        foreach (var hit in hits)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            await responseStream.WriteAsync(new SearchHit { Name = hit.Name, MatchKind = hit.MatchKind.ToString() })
                .ConfigureAwait(false);
        }
    }
}
