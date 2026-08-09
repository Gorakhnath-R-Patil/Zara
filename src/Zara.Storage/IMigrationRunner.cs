using Microsoft.Data.Sqlite;

namespace Zara.Storage;

/// <summary>
/// Applies forward-only, numbered SQL migrations tracked via
/// <c>PRAGMA user_version</c> — ARCHITECTURE.md §22's migration strategy.
/// Idempotent: safe to call every startup, including against a fresh,
/// empty database file.
/// </summary>
public interface IMigrationRunner
{
    /// <summary>Brings <paramref name="connection"/>'s schema up to the latest
    /// embedded migration. Call once, on one connection, before any other
    /// connection touches the database.</summary>
    void MigrateToLatest(SqliteConnection connection);
}
