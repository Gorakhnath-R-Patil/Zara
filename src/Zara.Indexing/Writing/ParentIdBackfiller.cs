using Zara.Storage;

namespace Zara.Indexing.Writing;

/// <inheritdoc cref="IParentIdBackfiller"/>
public sealed class ParentIdBackfiller : IParentIdBackfiller
{
    private readonly IWriteQueue _writeQueue;

    public ParentIdBackfiller(IWriteQueue writeQueue)
    {
        _writeQueue = writeQueue ?? throw new ArgumentNullException(nameof(writeQueue));
    }

    public Task<int> RunAsync(long volumeId, CancellationToken cancellationToken = default) =>
        _writeQueue.RunAsync(connection =>
        {
            using var cmd = connection.CreateCommand();

            // The correlated subquery resolves to the SAME result no matter
            // what order rows were originally written in — that's the whole
            // point of matching on parent_path_hash rather than requiring a
            // parent's id to have been known at write time. Rows whose
            // parent_path_hash doesn't match anything (the scan root itself,
            // which is deliberately never indexed — see FileIndexWriter's
            // remarks) resolve to NULL, correctly.
            cmd.CommandText = """
                UPDATE files
                SET parent_id = (
                    SELECT p.id FROM files AS p
                    WHERE p.volume_id = files.volume_id AND p.path_hash = files.parent_path_hash
                    LIMIT 1
                )
                WHERE volume_id = $volumeId AND parent_path_hash IS NOT NULL;
                """;
            cmd.Parameters.AddWithValue("$volumeId", volumeId);
            return cmd.ExecuteNonQuery();
        }, cancellationToken);
}
