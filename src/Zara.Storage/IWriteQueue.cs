using Microsoft.Data.Sqlite;

namespace Zara.Storage;

/// <summary>
/// Serializes every write to Zara's metadata database through one dedicated
/// connection on one background task — ARCHITECTURE.md §9.3: "All writes
/// serialized through one thread. SQLite WAL allows concurrent readers but a
/// single writer; enforcing it in code avoids SQLITE_BUSY entirely." Reads
/// don't go through here — they use their own connections from
/// <see cref="ISqliteConnectionFactory"/> directly, since WAL mode lets them
/// run concurrently with whatever this queue is doing.
/// </summary>
public interface IWriteQueue : IAsyncDisposable
{
    /// <summary>Queues a write and returns once it has actually run (not just
    /// been enqueued) — so callers that need to know a write landed before
    /// proceeding (e.g. before reporting scan progress) can simply await this.</summary>
    Task RunAsync(Action<SqliteConnection> write, CancellationToken cancellationToken = default);

    /// <summary>As <see cref="RunAsync(Action{SqliteConnection}, CancellationToken)"/>,
    /// for a write that produces a value (e.g. the id of a newly inserted row).</summary>
    Task<T> RunAsync<T>(Func<SqliteConnection, T> write, CancellationToken cancellationToken = default);
}
