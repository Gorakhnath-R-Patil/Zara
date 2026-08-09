using Zara.Volumes.Fallback;

namespace Zara.Indexing.Writing;

/// <summary>
/// Writes a batch of walked entries into the <c>files</c> table, upserting on
/// the <c>(volume_id, frn)</c> identity (ARCHITECTURE.md §10.1) so re-scanning
/// an already-indexed file updates it in place rather than duplicating it.
/// </summary>
/// <remarks>
/// Requires a non-null <see cref="WalkedFile.Frn"/> — entries from
/// <c>Win32DirectoryEnumerator</c> (which never provides one) are counted as
/// skipped, not written. FRN-less indexing would need a different identity
/// key entirely (there's no other stable one to upsert on), which is out of
/// scope for the walk-based M2 indexer; it's a real gap for non-NTFS volumes,
/// tracked rather than silently papered over.
/// </remarks>
public interface IFileIndexWriter
{
    Task<BatchWriteResult> UpsertBatchAsync(long volumeId, IReadOnlyList<WalkedFile> batch, CancellationToken cancellationToken = default);
}
