using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zara.Filesystem.Paths;
using Zara.Indexing.Scan;
using Zara.Storage;

namespace Zara.Engine.Hosting;

/// <summary>
/// Runs one <see cref="IScanOrchestrator"/> pass over a configured root at
/// Engine startup — the piece that makes T39 real rather than aspirational:
/// without this, <c>NameIndex</c>/the <c>files</c> table only ever contain
/// whatever a caller happens to have upserted directly (as every M7 test
/// through T38 did). Opt-in via <c>--scan-root=...</c>; when not supplied,
/// this is a no-op — every existing Engine test's behavior (an
/// intentionally-empty index) is unaffected.
/// </summary>
internal sealed class StartupScanHostedService : IHostedService
{
    private readonly IScanOrchestrator _orchestrator;
    private readonly EngineIndexState _state;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly string? _scanRoot;
    private readonly ILogger<StartupScanHostedService> _logger;

    private CancellationTokenSource? _cts;
    private Task? _scanTask;

    public StartupScanHostedService(
        IScanOrchestrator orchestrator,
        EngineIndexState state,
        ISqliteConnectionFactory connectionFactory,
        string? scanRoot,
        ILogger<StartupScanHostedService> logger)
    {
        _orchestrator = orchestrator;
        _state = state;
        _connectionFactory = connectionFactory;
        _scanRoot = scanRoot;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_scanRoot))
        {
            return Task.CompletedTask;
        }

        // Fire-and-forget relative to host startup — StartAsync must return
        // promptly (§24.2: nothing on the startup path blocks on indexing);
        // the scan runs in the background exactly as ARCHITECTURE.md's
        // first-run flow describes.
        _cts = new CancellationTokenSource();
        _scanTask = Task.Run(() => RunScanAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunScanAsync(CancellationToken cancellationToken)
    {
        _state.IsScanning = true;
        try
        {
            long volumeId = EnsureVolumeRow();
            var root = new PathCanonicalizer().CanonicalizeExisting(_scanRoot!);

            await _orchestrator.ScanAsync(volumeId, root, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on a clean shutdown mid-scan — not an error.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup scan of '{ScanRoot}' failed.", _scanRoot);
        }
        finally
        {
            _state.IsScanning = false;
        }
    }

    /// <summary>The scan orchestrator needs a <c>volumes</c> row id — this
    /// is a placeholder single-volume registration (guid <c>"startup-scan"</c>)
    /// good enough for M7's purposes; real multi-volume discovery is Phase 2
    /// scope (§11.3), not part of proving the process split works.</summary>
    private long EnsureVolumeRow()
    {
        using var connection = _connectionFactory.CreateConnection();

        using (var lookup = connection.CreateCommand())
        {
            lookup.CommandText = "SELECT id FROM volumes WHERE guid = 'startup-scan';";
            if (lookup.ExecuteScalar() is { } existingId)
            {
                return Convert.ToInt64(existingId);
            }
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO volumes (guid, serial, filesystem) VALUES ('startup-scan', 1, 'NTFS');
            SELECT last_insert_rowid();
            """;
        return (long)insert.ExecuteScalar()!;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();

        if (_scanTask is not null)
        {
            try
            {
                await _scanTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
