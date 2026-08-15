using Zara.Core.Files;
using Zara.Indexing.Writing;
using Zara.Search.Names;
using Zara.Volumes.Fallback;

namespace Zara.Engine.Hosting;

/// <summary>
/// Decorates the real <see cref="FileIndexWriter"/> so every batch written
/// to SQLite also updates the in-memory <see cref="INameIndex"/> — keeping
/// the two stores in sync without <c>Zara.Indexing</c> and <c>Zara.Search</c>
/// needing to know about each other (neither references the other, by
/// design — §8.2's module boundaries). Only the Engine, which already
/// depends on both, does this wiring — the same reasoning already applied
/// to why <c>Zara.Operations</c> exists as its own project rather than
/// living inside either <c>Zara.Storage</c> or <c>Zara.Filesystem</c>.
/// </summary>
internal sealed class NameIndexSyncingFileIndexWriter : IFileIndexWriter
{
    private readonly IFileIndexWriter _inner;
    private readonly INameIndex _nameIndex;

    public NameIndexSyncingFileIndexWriter(IFileIndexWriter inner, INameIndex nameIndex)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _nameIndex = nameIndex ?? throw new ArgumentNullException(nameof(nameIndex));
    }

    public async Task<BatchWriteResult> UpsertBatchAsync(
        long volumeId, IReadOnlyList<WalkedFile> batch, CancellationToken cancellationToken = default)
    {
        var result = await _inner.UpsertBatchAsync(volumeId, batch, cancellationToken).ConfigureAwait(false);

        // Same (volume_id, frn) identity FileIndexWriter itself upserts on
        // (§10.1) — entries without a FRN are the ones FileIndexWriter
        // already counted as skipped, so they're skipped here too.
        foreach (var entry in batch)
        {
            if (entry.Frn is { } frn)
            {
                _nameIndex.Upsert(new FileId(volumeId, unchecked((long)frn)), entry.Name);
            }
        }

        return result;
    }
}
