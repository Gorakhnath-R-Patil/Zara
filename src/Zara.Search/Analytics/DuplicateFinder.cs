using Microsoft.Data.Sqlite;

namespace Zara.Search.Analytics;

/// <inheritdoc cref="IDuplicateFinder"/>
public sealed class DuplicateFinder : IDuplicateFinder
{
    public async Task<IReadOnlyList<DuplicateGroup>> FindCandidatesAsync(
        SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Pulled into memory and grouped client-side (LINQ) rather than a
        // SQL GROUP_CONCAT aggregation: a name could contain any separator
        // character SQL-side concatenation would pick, and correctness here
        // matters more than the (small, infrequent — this isn't a hot path)
        // cost of materializing rows.
        var rows = new List<(long Id, string Name, long Size, long QuickHash)>();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, name, size_bytes, quick_hash
                FROM files
                WHERE is_dir = 0 AND quick_hash IS NOT NULL AND deleted_utc IS NULL;
                """;

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)));
            }
        }

        return rows
            .GroupBy(r => (r.Size, r.QuickHash))
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup(
                g.Key.Size,
                g.Select(r => new DuplicateFile(r.Id, r.Name)).ToList()))
            .ToList();
    }

    public IReadOnlyList<DuplicateGroup> Confirm(DuplicateGroup candidate, Func<long, string> computeContentHash)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(computeContentHash);

        return candidate.Files
            .GroupBy(f => computeContentHash(f.FileId), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup(candidate.SizeBytes, g.ToList()))
            .ToList();
    }
}
