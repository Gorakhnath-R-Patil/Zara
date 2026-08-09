using Microsoft.Data.Sqlite;
using Zara.Indexing.Hashing;
using Zara.Storage;
using Zara.Volumes.Fallback;

namespace Zara.Indexing.Writing;

/// <inheritdoc cref="IFileIndexWriter"/>
public sealed class FileIndexWriter : IFileIndexWriter
{
    private readonly IWriteQueue _writeQueue;

    public FileIndexWriter(IWriteQueue writeQueue)
    {
        _writeQueue = writeQueue ?? throw new ArgumentNullException(nameof(writeQueue));
    }

    public Task<BatchWriteResult> UpsertBatchAsync(
        long volumeId, IReadOnlyList<WalkedFile> batch, CancellationToken cancellationToken = default) =>
        _writeQueue.RunAsync(connection => WriteBatch(connection, volumeId, batch), cancellationToken);

    private static BatchWriteResult WriteBatch(SqliteConnection connection, long volumeId, IReadOnlyList<WalkedFile> batch)
    {
        int written = 0;
        int skipped = 0;
        long nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;

        // Upsert on (volume_id, frn) — the true, rename/move-surviving
        // identity (ARCHITECTURE.md §10.1) — rather than on path, so
        // re-scanning a moved/renamed file updates the existing row instead
        // of creating a duplicate.
        cmd.CommandText = """
            INSERT INTO files (volume_id, frn, name, name_folded, ext, path_hash, parent_path_hash, depth, is_dir,
                                size_bytes, created_utc, modified_utc, accessed_utc, attributes, indexed_utc)
            VALUES ($volumeId, $frn, $name, $nameFolded, $ext, $pathHash, $parentPathHash, $depth, $isDir,
                    $size, $created, $modified, $accessed, $attributes, $indexedUtc)
            ON CONFLICT(volume_id, frn) DO UPDATE SET
                name             = excluded.name,
                name_folded      = excluded.name_folded,
                ext              = excluded.ext,
                path_hash        = excluded.path_hash,
                parent_path_hash = excluded.parent_path_hash,
                depth            = excluded.depth,
                is_dir           = excluded.is_dir,
                size_bytes       = excluded.size_bytes,
                created_utc      = excluded.created_utc,
                modified_utc     = excluded.modified_utc,
                accessed_utc     = excluded.accessed_utc,
                attributes       = excluded.attributes,
                indexed_utc      = excluded.indexed_utc,
                deleted_utc      = NULL;
            """;

        var pVolumeId = cmd.Parameters.Add("$volumeId", SqliteType.Integer);
        var pFrn = cmd.Parameters.Add("$frn", SqliteType.Integer);
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pNameFolded = cmd.Parameters.Add("$nameFolded", SqliteType.Text);
        var pExt = cmd.Parameters.Add("$ext", SqliteType.Text);
        var pPathHash = cmd.Parameters.Add("$pathHash", SqliteType.Integer);
        var pParentPathHash = cmd.Parameters.Add("$parentPathHash", SqliteType.Integer);
        var pDepth = cmd.Parameters.Add("$depth", SqliteType.Integer);
        var pIsDir = cmd.Parameters.Add("$isDir", SqliteType.Integer);
        var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
        var pCreated = cmd.Parameters.Add("$created", SqliteType.Integer);
        var pModified = cmd.Parameters.Add("$modified", SqliteType.Integer);
        var pAccessed = cmd.Parameters.Add("$accessed", SqliteType.Integer);
        var pAttributes = cmd.Parameters.Add("$attributes", SqliteType.Integer);
        var pIndexedUtc = cmd.Parameters.Add("$indexedUtc", SqliteType.Integer);
        cmd.Prepare();

        pVolumeId.Value = volumeId;

        foreach (var entry in batch)
        {
            if (entry.Frn is not { } frn)
            {
                skipped++;
                continue;
            }

            pFrn.Value = unchecked((long)frn);
            pName.Value = entry.Name;
            pNameFolded.Value = entry.Name.ToLowerInvariant();
            pExt.Value = (object?)GetExtension(entry.Name) ?? DBNull.Value;
            pPathHash.Value = PathHasher.Compute(entry.Path.Value);
            pParentPathHash.Value = ComputeParentPathHash(entry.Path.Value);
            pDepth.Value = entry.Depth;
            pIsDir.Value = entry.IsDirectory ? 1 : 0;
            pSize.Value = entry.SizeBytes;
            pCreated.Value = ToUnixMillis(entry.CreatedUtc);
            pModified.Value = ToUnixMillis(entry.ModifiedUtc);
            pAccessed.Value = ToUnixMillis(entry.AccessedUtc);
            pAttributes.Value = (long)entry.Attributes;
            pIndexedUtc.Value = nowUtc;

            cmd.ExecuteNonQuery();
            written++;
        }

        transaction.Commit();
        return new BatchWriteResult(written, skipped);
    }

    private static object ToUnixMillis(DateTimeOffset? value) =>
        value.HasValue ? value.Value.ToUnixTimeMilliseconds() : DBNull.Value;

    /// <summary>
    /// Hashes the entry's PARENT path the same way <see cref="PathHasher"/>
    /// hashes its own path — see <c>003_parent_path_hash.sql</c>'s header
    /// comment for why this, rather than a direct id lookup, is what makes
    /// <c>parent_id</c> backfillable regardless of write order.
    /// </summary>
    private static object ComputeParentPathHash(string fullPath)
    {
        string? parent = Path.GetDirectoryName(fullPath);
        return parent is null ? DBNull.Value : PathHasher.Compute(parent);
    }

    private static string? GetExtension(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..].ToLowerInvariant() : null;
    }
}
