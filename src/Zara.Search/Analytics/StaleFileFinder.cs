using Microsoft.Data.Sqlite;

namespace Zara.Search.Analytics;

public sealed record StaleFile(long FileId, string Name, long ModifiedUtc, long SizeBytes);

/// <summary>
/// Files that look stale — ARCHITECTURE.md §32's "find files I haven't
/// opened in 6 months" use case.
/// </summary>
/// <remarks>
/// <b>Uses <c>modified_utc</c>, not <c>accessed_utc</c>, as the staleness
/// signal.</b> NTFS last-access-time tracking (<c>NtfsDisableLastAccessUpdate</c>)
/// has been disabled by default since Windows Vista for performance reasons —
/// <c>accessed_utc</c> on a typical Windows 11 machine is unreliable or
/// simply not updated at all. Real "haven't opened this in 6 months"
/// tracking needs the app's own open-event log (recorded when a user opens
/// a file through Zara itself), which doesn't exist yet — this is exactly
/// the kind of unreliable-signal problem ARCHITECTURE.md §1 (challenge the
/// idea) calls out. <c>modified_utc</c> is the closest reliable proxy
/// available today; results should be presented to the user as "not
/// modified since", not "not opened since", to avoid promising precision
/// the underlying data can't back up.
/// </remarks>
public interface IStaleFileFinder
{
    Task<IReadOnlyList<StaleFile>> FindAsync(
        SqliteConnection connection, long volumeId, TimeSpan olderThan, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IStaleFileFinder"/>
public sealed class StaleFileFinder : IStaleFileFinder
{
    public async Task<IReadOnlyList<StaleFile>> FindAsync(
        SqliteConnection connection, long volumeId, TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        long threshold = DateTimeOffset.UtcNow.Subtract(olderThan).ToUnixTimeMilliseconds();
        var results = new List<StaleFile>();

        using var cmd = connection.CreateCommand();
        // Largest-first: pairs naturally with "old AND large" — the files
        // most worth a user's attention when cleaning up — without needing
        // a separate large-file query.
        cmd.CommandText = """
            SELECT id, name, modified_utc, size_bytes
            FROM files
            WHERE volume_id = $volumeId AND is_dir = 0 AND deleted_utc IS NULL
              AND modified_utc IS NOT NULL AND modified_utc < $threshold
            ORDER BY size_bytes DESC;
            """;
        cmd.Parameters.AddWithValue("$volumeId", volumeId);
        cmd.Parameters.AddWithValue("$threshold", threshold);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new StaleFile(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)));
        }

        return results;
    }
}
