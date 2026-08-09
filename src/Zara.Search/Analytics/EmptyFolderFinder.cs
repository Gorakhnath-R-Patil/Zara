using Microsoft.Data.Sqlite;

namespace Zara.Search.Analytics;

public sealed record EmptyFolder(long FileId, string Name);

/// <summary>
/// Directories that contain no files anywhere in their subtree — depends on
/// <c>folder_stats.total_files</c> (populated by <see cref="ISizeRollup"/>,
/// which must have already run). "Empty" here means recursively empty: a
/// directory containing only other empty directories still counts, since
/// that whole branch is equally safe to clean up — see ARCHITECTURE.md
/// §32's "empty:true" predicate and the "Clean up my Downloads" use case.
/// </summary>
public interface IEmptyFolderFinder
{
    Task<IReadOnlyList<EmptyFolder>> FindAsync(SqliteConnection connection, long volumeId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IEmptyFolderFinder"/>
public sealed class EmptyFolderFinder : IEmptyFolderFinder
{
    public async Task<IReadOnlyList<EmptyFolder>> FindAsync(
        SqliteConnection connection, long volumeId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var results = new List<EmptyFolder>();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT f.id, f.name
            FROM files f
            JOIN folder_stats fs ON fs.file_id = f.id
            WHERE f.volume_id = $volumeId AND f.is_dir = 1 AND f.deleted_utc IS NULL
              AND fs.total_files = 0;
            """;
        cmd.Parameters.AddWithValue("$volumeId", volumeId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new EmptyFolder(reader.GetInt64(0), reader.GetString(1)));
        }

        return results;
    }
}
