using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zara.Contracts.Admin;
using Zara.Contracts.Indexing;
using Zara.Contracts.Journal;
using Zara.Contracts.Search;
using Zara.Core.Operations;
using Zara.Engine.Hosting;
using Zara.Engine.Services;
using Zara.Filesystem.Enumeration;
using Zara.Indexing.Checkpointing;
using Zara.Indexing.Scan;
using Zara.Indexing.Writing;
using Zara.Search.Names;
using Zara.Storage;
using Zara.Storage.Journal;
using Zara.Volumes.Fallback;

namespace Zara.Engine;

/// <summary>
/// Builds the Engine's Generic Host — extracted out of <c>Program.cs</c>
/// specifically so tests can build a real host against an isolated temp
/// database and a unique pipe name, rather than only being able to exercise
/// this through the actual running process.
/// </summary>
public static class EngineHost
{
    /// <param name="scanRoot">If supplied, <see cref="StartupScanHostedService"/>
    /// runs one real <see cref="ScanOrchestrator"/> pass over this directory in
    /// the background, populating both the <c>files</c> table and the
    /// in-memory <c>NameIndex</c> (kept in sync via
    /// <see cref="NameIndexSyncingFileIndexWriter"/>) — this is what makes
    /// T39 an actually-wired pipeline rather than an empty index waiting for
    /// a caller to seed it by hand. Null (the default) skips scanning
    /// entirely, which is what every M7 test through T38/T40 relies on.</param>
    public static IHost Build(string[] args, string dbPath, string pipeName, string? scanRoot = null)
    {
        var builder = Host.CreateApplicationBuilder(args);

        var connectionFactory = new SqliteConnectionFactory(dbPath);
        using (var bootstrapConnection = connectionFactory.CreateConnection())
        {
            new MigrationRunner().MigrateToLatest(bootstrapConnection);
        }

        builder.Services.AddSingleton<ISqliteConnectionFactory>(connectionFactory);
        builder.Services.AddSingleton<IWriteQueue>(sp => new WriteQueue(sp.GetRequiredService<ISqliteConnectionFactory>()));
        builder.Services.AddSingleton<IOperationJournal, OperationJournal>();
        builder.Services.AddSingleton<INameIndex, NameIndex>();
        builder.Services.AddSingleton<EngineIndexState>();

        // Indexing pipeline — real scanning, not just a store waiting to be
        // seeded. IFileIndexWriter is wrapped so every SQLite write also
        // updates the shared NameIndex; see that type's remarks for why the
        // Engine (not Zara.Indexing or Zara.Search) is where this wiring lives.
        builder.Services.AddSingleton<IDirectoryEnumerator, NtDirectoryEnumerator>();
        builder.Services.AddSingleton<IWalkScanner>(sp => new WalkScanner(sp.GetRequiredService<IDirectoryEnumerator>()));
        builder.Services.AddSingleton<IScanCheckpointStore>(sp => new ScanCheckpointStore(
            sp.GetRequiredService<ISqliteConnectionFactory>(), sp.GetRequiredService<IWriteQueue>()));
        builder.Services.AddSingleton<IFileIndexWriter>(sp => new NameIndexSyncingFileIndexWriter(
            new FileIndexWriter(sp.GetRequiredService<IWriteQueue>()), sp.GetRequiredService<INameIndex>()));
        builder.Services.AddSingleton<IScanOrchestrator>(sp => new ScanOrchestrator(
            sp.GetRequiredService<IDirectoryEnumerator>(),
            sp.GetRequiredService<IWalkScanner>(),
            sp.GetRequiredService<IFileIndexWriter>(),
            sp.GetRequiredService<IScanCheckpointStore>()));

        builder.Services.AddSingleton<AdminServiceImpl>();
        builder.Services.AddSingleton<SearchServiceImpl>();
        builder.Services.AddSingleton<IndexServiceImpl>();
        builder.Services.AddSingleton<JournalServiceImpl>();

        builder.Services.AddSingleton<IHostedService>(sp => new GrpcNamedPipeHostedService(
            pipeName,
            binder =>
            {
                AdminService.BindService(binder, sp.GetRequiredService<AdminServiceImpl>());
                SearchService.BindService(binder, sp.GetRequiredService<SearchServiceImpl>());
                IndexService.BindService(binder, sp.GetRequiredService<IndexServiceImpl>());
                JournalService.BindService(binder, sp.GetRequiredService<JournalServiceImpl>());
            },
            sp.GetRequiredService<ILogger<GrpcNamedPipeHostedService>>()));

        builder.Services.AddSingleton<IHostedService>(sp => new StartupScanHostedService(
            sp.GetRequiredService<IScanOrchestrator>(),
            sp.GetRequiredService<EngineIndexState>(),
            sp.GetRequiredService<ISqliteConnectionFactory>(),
            scanRoot,
            sp.GetRequiredService<ILogger<StartupScanHostedService>>()));

        return builder.Build();
    }

    /// <summary>ARCHITECTURE.md §17.5: <c>%LOCALAPPDATA%\Zara\zara.db</c>.</summary>
    public static string DefaultDbPath()
    {
        string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zara");
        Directory.CreateDirectory(dataDirectory);
        return Path.Combine(dataDirectory, "zara.db");
    }
}
