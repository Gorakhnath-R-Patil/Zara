using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace Zara.Storage;

/// <inheritdoc cref="IWriteQueue"/>
public sealed class WriteQueue : IWriteQueue
{
    // Bounded, per ARCHITECTURE.md §25.2: "Every queue in the system is
    // bounded... checked by a test that asserts no Channel.CreateUnbounded
    // call exists outside a small allowlist." A slow or stuck writer should
    // apply backpressure to callers (BoundedChannelFullMode.Wait), not let
    // queued work grow without limit.
    private const int DefaultCapacity = 4096;

    private readonly SqliteConnection _connection;
    private readonly Channel<WorkItem> _channel;
    private readonly Task _worker;
    private int _disposed;

    public WriteQueue(ISqliteConnectionFactory connectionFactory, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _connection = connectionFactory.CreateConnection();
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _worker = Task.Run(ProcessLoopAsync);
    }

    public Task RunAsync(Action<SqliteConnection> write, CancellationToken cancellationToken = default) =>
        RunAsync<object?>(connection =>
        {
            write(connection);
            return null;
        }, cancellationToken);

    public async Task<T> RunAsync<T>(Func<SqliteConnection, T> write, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new WorkItem(connection => write(connection), completion);

        await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);

        object? result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return (T)result!;
    }

    private async Task ProcessLoopAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                object? result = item.Work(_connection);
                item.Completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                item.Completion.TrySetException(ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.Complete();
        await _worker.ConfigureAwait(false);
        _connection.Dispose();
    }

    private readonly record struct WorkItem(Func<SqliteConnection, object?> Work, TaskCompletionSource<object?> Completion);
}
