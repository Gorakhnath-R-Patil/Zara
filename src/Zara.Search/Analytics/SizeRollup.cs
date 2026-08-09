using Microsoft.Data.Sqlite;

namespace Zara.Search.Analytics;

/// <inheritdoc cref="ISizeRollup"/>
/// <remarks>
/// Full recomputation, not incremental: loads every live row for a volume,
/// folds direct files into their parent's running total, then propagates
/// each directory's fully-accumulated total up to ITS parent in a single
/// pass ordered by depth descending (deepest first) — a directory's rollup
/// is always complete before it's folded into its own parent. O(n) after the
/// sort, one pass, no recursive SQL needed.
///
/// This is the simpler baseline ARCHITECTURE.md §22 explicitly contrasts
/// with the incremental <c>dirty</c>-flag propagation design meant for
/// production scale (a single file changing shouldn't require re-scanning
/// an entire volume's rollups). Full recomputation is correct and easy to
/// reason about; swap in incremental maintenance once profiling on a large
/// corpus shows this pass costing enough to matter — not before.
/// </remarks>
public sealed class SizeRollup : ISizeRollup
{
    private readonly record struct Row(long Id, long? ParentId, bool IsDirectory, long SizeBytes, long? ModifiedUtc, int Depth);

    private sealed class Accumulator
    {
        public int DirectFiles;
        public long TotalFiles;
        public long TotalBytes;
        public long? MaxModifiedUtc;
    }

    public async Task<int> RecomputeAsync(SqliteConnection connection, long volumeId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var rows = await LoadRowsAsync(connection, volumeId, cancellationToken).ConfigureAwait(false);
        var stats = Accumulate(rows);
        return Persist(connection, stats);
    }

    private static async Task<List<Row>> LoadRowsAsync(SqliteConnection connection, long volumeId, CancellationToken cancellationToken)
    {
        var rows = new List<Row>();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, parent_id, is_dir, size_bytes, modified_utc, depth
            FROM files
            WHERE volume_id = $volumeId AND deleted_utc IS NULL;
            """;
        cmd.Parameters.AddWithValue("$volumeId", volumeId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new Row(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.GetInt64(2) != 0,
                reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.GetInt32(5)));
        }

        return rows;
    }

    private static Dictionary<long, Accumulator> Accumulate(List<Row> rows)
    {
        var stats = new Dictionary<long, Accumulator>();

        foreach (var dir in rows.Where(r => r.IsDirectory))
        {
            stats[dir.Id] = new Accumulator();
        }

        // Fold each file's size/mtime into its immediate parent's running total.
        foreach (var file in rows.Where(r => !r.IsDirectory && r.ParentId is not null))
        {
            if (!stats.TryGetValue(file.ParentId!.Value, out var acc))
            {
                continue; // parent_path_hash didn't resolve to a live directory row
            }

            acc.DirectFiles++;
            acc.TotalFiles++;
            acc.TotalBytes += file.SizeBytes;
            acc.MaxModifiedUtc = MaxOf(acc.MaxModifiedUtc, file.ModifiedUtc);
        }

        // Propagate each directory's now-complete rollup up into its own
        // parent, deepest directories first so nothing is folded upward
        // before it's finished accumulating.
        foreach (var dir in rows.Where(r => r.IsDirectory).OrderByDescending(r => r.Depth))
        {
            if (dir.ParentId is not { } parentId || !stats.TryGetValue(parentId, out var parentAcc))
            {
                continue;
            }

            var own = stats[dir.Id];
            parentAcc.TotalFiles += own.TotalFiles;
            parentAcc.TotalBytes += own.TotalBytes;
            parentAcc.MaxModifiedUtc = MaxOf(parentAcc.MaxModifiedUtc, own.MaxModifiedUtc);
        }

        return stats;
    }

    private static int Persist(SqliteConnection connection, Dictionary<long, Accumulator> stats)
    {
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO folder_stats (file_id, direct_files, total_files, total_bytes, max_modified_utc, dirty)
            VALUES ($id, $directFiles, $totalFiles, $totalBytes, $maxModified, 0)
            ON CONFLICT(file_id) DO UPDATE SET
                direct_files     = excluded.direct_files,
                total_files      = excluded.total_files,
                total_bytes      = excluded.total_bytes,
                max_modified_utc = excluded.max_modified_utc,
                dirty            = 0;
            """;

        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
        var pDirectFiles = cmd.Parameters.Add("$directFiles", SqliteType.Integer);
        var pTotalFiles = cmd.Parameters.Add("$totalFiles", SqliteType.Integer);
        var pTotalBytes = cmd.Parameters.Add("$totalBytes", SqliteType.Integer);
        var pMaxModified = cmd.Parameters.Add("$maxModified", SqliteType.Integer);
        cmd.Prepare();

        int written = 0;
        foreach (var (id, acc) in stats)
        {
            pId.Value = id;
            pDirectFiles.Value = acc.DirectFiles;
            pTotalFiles.Value = acc.TotalFiles;
            pTotalBytes.Value = acc.TotalBytes;
            pMaxModified.Value = (object?)acc.MaxModifiedUtc ?? DBNull.Value;
            cmd.ExecuteNonQuery();
            written++;
        }

        transaction.Commit();
        return written;
    }

    private static long? MaxOf(long? a, long? b) => (a, b) switch
    {
        (null, _) => b,
        (_, null) => a,
        _ => Math.Max(a.Value, b.Value),
    };
}
