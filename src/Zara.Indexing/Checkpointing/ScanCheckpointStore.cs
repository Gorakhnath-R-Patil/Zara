using Microsoft.Data.Sqlite;
using Zara.Storage;

namespace Zara.Indexing.Checkpointing;

/// <inheritdoc cref="IScanCheckpointStore"/>
public sealed class ScanCheckpointStore : IScanCheckpointStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IWriteQueue _writeQueue;

    public ScanCheckpointStore(ISqliteConnectionFactory connectionFactory, IWriteQueue writeQueue)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _writeQueue = writeQueue ?? throw new ArgumentNullException(nameof(writeQueue));
    }

    public Task<IReadOnlySet<string>> GetCompletedChildrenAsync(
        long volumeId, string rootPath, CancellationToken cancellationToken = default)
    {
        // Reads go through their own connection, not the write queue — WAL
        // mode lets this run concurrently with whatever the writer is doing
        // (ARCHITECTURE.md §9.3).
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT child_name FROM scan_checkpoints WHERE volume_id = $volumeId AND root_path = $rootPath;";
        cmd.Parameters.AddWithValue("$volumeId", volumeId);
        cmd.Parameters.AddWithValue("$rootPath", rootPath);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return Task.FromResult<IReadOnlySet<string>>(result);
    }

    public Task MarkChildCompletedAsync(
        long volumeId, string rootPath, string childName, CancellationToken cancellationToken = default) =>
        _writeQueue.RunAsync(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO scan_checkpoints (volume_id, root_path, child_name, completed_utc)
                VALUES ($volumeId, $rootPath, $childName, $completedUtc)
                ON CONFLICT(volume_id, root_path, child_name) DO UPDATE SET completed_utc = excluded.completed_utc;
                """;
            cmd.Parameters.AddWithValue("$volumeId", volumeId);
            cmd.Parameters.AddWithValue("$rootPath", rootPath);
            cmd.Parameters.AddWithValue("$childName", childName);
            cmd.Parameters.AddWithValue("$completedUtc", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            cmd.ExecuteNonQuery();
        }, cancellationToken);

    public Task ClearAsync(long volumeId, string rootPath, CancellationToken cancellationToken = default) =>
        _writeQueue.RunAsync(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM scan_checkpoints WHERE volume_id = $volumeId AND root_path = $rootPath;";
            cmd.Parameters.AddWithValue("$volumeId", volumeId);
            cmd.Parameters.AddWithValue("$rootPath", rootPath);
            cmd.ExecuteNonQuery();
        }, cancellationToken);
}
