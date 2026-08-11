using Microsoft.Data.Sqlite;
using Zara.Core.Operations;

namespace Zara.Storage.Journal;

/// <inheritdoc cref="IOperationJournal"/>
public sealed class OperationJournal : IOperationJournal
{
    private readonly IWriteQueue _writeQueue;
    private readonly ISqliteConnectionFactory _connectionFactory;

    public OperationJournal(IWriteQueue writeQueue, ISqliteConnectionFactory connectionFactory)
    {
        _writeQueue = writeQueue ?? throw new ArgumentNullException(nameof(writeQueue));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task<Guid> BeginAsync(OperationPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return _writeQueue.RunAsync(connection =>
        {
            // Time-ordered — a UUIDv7 sorts the same as created_utc would,
            // which is convenient for the undo stack's "most recent first"
            // ordering (§19.5) even though the query itself orders by
            // created_utc explicitly.
            var id = Guid.CreateVersion7();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // One transaction — operation row AND every item row land
            // together, or not at all. This is the "durably record intent
            // BEFORE anything executes" half of §19.3's write protocol.
            using var transaction = connection.BeginTransaction();

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    INSERT INTO operations (id, kind, status, created_utc, item_count, total_bytes,
                                             risk_class, confirmed_by_user, user_request)
                    VALUES ($id, $kind, 'planned', $createdUtc, $itemCount, 0, $riskClass, $confirmed, $userRequest);
                    """;
                cmd.Parameters.AddWithValue("$id", id.ToString());
                cmd.Parameters.AddWithValue("$kind", plan.Kind.ToString().ToLowerInvariant());
                cmd.Parameters.AddWithValue("$createdUtc", now);
                cmd.Parameters.AddWithValue("$itemCount", plan.Items.Count);
                cmd.Parameters.AddWithValue("$riskClass", plan.RiskClass);
                cmd.Parameters.AddWithValue("$confirmed", plan.ConfirmedByUser ? 1 : 0);
                cmd.Parameters.AddWithValue("$userRequest", (object?)plan.UserRequest ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            using (var itemCmd = connection.CreateCommand())
            {
                itemCmd.Transaction = transaction;
                itemCmd.CommandText = """
                    INSERT INTO operation_items (operation_id, seq, source_path, dest_path, status)
                    VALUES ($opId, $seq, $source, $dest, 'pending');
                    """;
                var pOpId = itemCmd.Parameters.Add("$opId", SqliteType.Text);
                var pSeq = itemCmd.Parameters.Add("$seq", SqliteType.Integer);
                var pSource = itemCmd.Parameters.Add("$source", SqliteType.Text);
                var pDest = itemCmd.Parameters.Add("$dest", SqliteType.Text);
                itemCmd.Prepare();

                pOpId.Value = id.ToString();

                for (int i = 0; i < plan.Items.Count; i++)
                {
                    pSeq.Value = i;
                    pSource.Value = plan.Items[i].SourcePath.Value;
                    pDest.Value = (object?)plan.Items[i].DestPath?.Value ?? DBNull.Value;
                    itemCmd.ExecuteNonQuery();
                }
            }

            transaction.Commit();
            return id;
        }, cancellationToken);
    }

    public Task MarkExecutingAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        _writeQueue.RunAsync(connection => UpdateStatus(connection, operationId, "executing", setCompletedUtc: false), cancellationToken);

    public Task RecordItemAsync(Guid operationId, OperationItemOutcome outcome, CancellationToken cancellationToken = default) =>
        _writeQueue.RunAsync(connection =>
        {
            using var cmd = connection.CreateCommand();
            // COALESCE($dest, dest_path): BeginAsync already recorded the
            // PLANNED destination; only overwrite it if this outcome
            // supplies an actual one (e.g. renamed-on-conflict).
            cmd.CommandText = """
                UPDATE operation_items
                SET status = $status, dest_path = COALESCE($dest, dest_path), recycle_id = $recycleId, error_code = $errorCode
                WHERE operation_id = $opId AND seq = $seq;
                """;
            cmd.Parameters.AddWithValue("$status", outcome.Status.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("$dest", (object?)outcome.DestPath?.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$recycleId", (object?)outcome.RecycleId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$errorCode", (object?)outcome.ErrorCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$opId", operationId.ToString());
            cmd.Parameters.AddWithValue("$seq", outcome.Seq);
            cmd.ExecuteNonQuery();
        }, cancellationToken);

    public Task CompleteAsync(Guid operationId, OperationStatus status, CancellationToken cancellationToken = default) =>
        _writeQueue.RunAsync(
            connection => UpdateStatus(connection, operationId, status.ToString().ToLowerInvariant(), setCompletedUtc: true),
            cancellationToken);

    private static void UpdateStatus(SqliteConnection connection, Guid operationId, string status, bool setCompletedUtc)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = setCompletedUtc
            ? "UPDATE operations SET status = $status, completed_utc = $now WHERE id = $id;"
            : "UPDATE operations SET status = $status WHERE id = $id;";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$id", operationId.ToString());
        if (setCompletedUtc)
        {
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        cmd.ExecuteNonQuery();
    }

    public async Task<OperationRecord?> GetAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, kind, status, created_utc, completed_utc, item_count, total_bytes, risk_class, confirmed_by_user, user_request
            FROM operations WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", operationId.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadOperation(reader) : null;
    }

    public async Task<IReadOnlyList<OperationItemRecord>> GetItemsAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT operation_id, seq, source_path, dest_path, status, recycle_id, error_code
            FROM operation_items WHERE operation_id = $id ORDER BY seq;
            """;
        cmd.Parameters.AddWithValue("$id", operationId.ToString());

        var results = new List<OperationItemRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadItem(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<OperationRecord>> GetInterruptedAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, kind, status, created_utc, completed_utc, item_count, total_bytes, risk_class, confirmed_by_user, user_request
            FROM operations WHERE status = 'executing' ORDER BY created_utc;
            """;

        var results = new List<OperationRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadOperation(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<OperationRecord>> GetUndoableAsync(int limit, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, kind, status, created_utc, completed_utc, item_count, total_bytes, risk_class, confirmed_by_user, user_request
            FROM operations WHERE status IN ('completed', 'partial') ORDER BY created_utc DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<OperationRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadOperation(reader));
        }

        return results;
    }

    private static OperationRecord ReadOperation(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Enum.Parse<OperationKind>(reader.GetString(1), ignoreCase: true),
        Enum.Parse<OperationStatus>(reader.GetString(2), ignoreCase: true),
        reader.GetInt64(3),
        reader.IsDBNull(4) ? null : reader.GetInt64(4),
        reader.GetInt32(5),
        reader.GetInt64(6),
        reader.GetString(7),
        reader.GetInt32(8) != 0,
        reader.IsDBNull(9) ? null : reader.GetString(9));

    private static OperationItemRecord ReadItem(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetInt32(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        Enum.Parse<OperationItemStatus>(reader.GetString(4), ignoreCase: true),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6));
}
