using Grpc.Core;
using Zara.Contracts.Indexing;
using Zara.Engine.Hosting;
using Zara.Search.Names;

namespace Zara.Engine.Services;

public sealed class IndexServiceImpl : IndexService.IndexServiceBase
{
    private readonly INameIndex _nameIndex;
    private readonly EngineIndexState _state;

    public IndexServiceImpl(INameIndex nameIndex, EngineIndexState state)
    {
        _nameIndex = nameIndex ?? throw new ArgumentNullException(nameof(nameIndex));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public override Task<IndexStatusResponse> GetStatus(GetStatusRequest request, ServerCallContext context) =>
        Task.FromResult(new IndexStatusResponse
        {
            IndexedFileCount = _nameIndex.Count,
            IsScanning = _state.IsScanning,
        });
}
