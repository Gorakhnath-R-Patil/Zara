using Microsoft.Data.Sqlite;

namespace Zara.Storage;

/// <summary>
/// Opens connections to Zara's metadata database with the standard pragma set
/// (ARCHITECTURE.md §6.7) already applied. Does not run migrations — call
/// <see cref="IMigrationRunner"/> explicitly once at startup, on one
/// connection, before any other connection is used.
/// </summary>
public interface ISqliteConnectionFactory
{
    /// <summary>Opens a new, pragma-configured connection. Callers own disposal.</summary>
    SqliteConnection CreateConnection();
}
