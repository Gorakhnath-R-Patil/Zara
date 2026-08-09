using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Zara.Core.Files;
using Zara.Filesystem.Enumeration;
using Zara.Filesystem.Paths;
using Zara.Indexing.Checkpointing;
using Zara.Indexing.Scan;
using Zara.Indexing.Writing;
using Zara.Storage;
using Zara.Volumes.Fallback;
using Zara.Volumes.Skip;

namespace Zara.Scenarios;

/// <summary>
/// T16 (ARCHITECTURE.md tracker M2): full-scan wall time and resumability,
/// against a real generated corpus and a real SQLite database — not a
/// simulation. See B01 (full scan) and the resumability half of §32.2 in
/// spirit; scaled to what's practical to run interactively rather than
/// BenchmarkDotNet's full statistical treatment (same rationale as B12 in
/// Program.cs).
/// </summary>
internal static class ScanScenario
{
    public static void Run(int totalFiles, int childDirCount)
    {
        string corpusRoot = Path.Combine(Path.GetTempPath(), $"zara-scenario-scan-{totalFiles}-{childDirCount}");
        Console.WriteLine($"T16: full scan + resumability, {totalFiles:N0} files across {childDirCount} directories");
        Console.WriteLine($"Corpus: {corpusRoot}");
        EnsureShardedCorpus(corpusRoot, totalFiles, childDirCount);

        RunFullScanTiming(corpusRoot, totalFiles, childDirCount);
        RunResumabilityTiming(corpusRoot, totalFiles, childDirCount);
    }

    private static void EnsureShardedCorpus(string root, int totalFiles, int childDirCount)
    {
        int filesPerChild = totalFiles / childDirCount;

        if (Directory.Exists(root) &&
            Directory.EnumerateDirectories(root).Count() == childDirCount &&
            Directory.EnumerateFiles(Directory.EnumerateDirectories(root).First()).Count() == filesPerChild)
        {
            Console.WriteLine("Corpus already present, reusing it.");
            return;
        }

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        Console.WriteLine("Generating sharded corpus...");
        var sw = Stopwatch.StartNew();
        byte[] content = new byte[64];

        for (int c = 0; c < childDirCount; c++)
        {
            string childDir = Path.Combine(root, $"shard-{c:D3}");
            Directory.CreateDirectory(childDir);
            for (int f = 0; f < filesPerChild; f++)
            {
                File.WriteAllBytes(Path.Combine(childDir, $"file-{f:D5}.dat"), content);
            }
        }

        sw.Stop();
        Console.WriteLine($"Corpus generated in {sw.Elapsed.TotalSeconds:F1}s.");
    }

    private static void RunFullScanTiming(string corpusRoot, int totalFiles, int childDirCount)
    {
        Console.WriteLine();
        Console.WriteLine("── Full scan ──");

        string dbPath = TempDbPath();
        try
        {
            var connectionFactory = new SqliteConnectionFactory(dbPath);
            Bootstrap(connectionFactory);
            long volumeId = InsertVolume(connectionFactory);

            var writeQueue = new WriteQueue(connectionFactory);
            var orchestrator = MakeOrchestrator(connectionFactory, writeQueue);
            var root = new PathCanonicalizer().CanonicalizeExisting(corpusRoot);

            var sw = Stopwatch.StartNew();
            var result = orchestrator.ScanAsync(volumeId, root).GetAwaiter().GetResult();
            sw.Stop();

            long expectedRows = totalFiles + childDirCount; // files + one row per shard directory itself
            Console.WriteLine($"  {result.FilesWritten:N0} rows written ({expectedRows:N0} expected), {result.FilesSkipped:N0} skipped");
            Console.WriteLine($"  wall time: {sw.Elapsed.TotalSeconds:F1}s ({result.FilesWritten / Math.Max(sw.Elapsed.TotalSeconds, 0.001):N0} rows/s)");
            Console.WriteLine($"  T16 correctness: {(result.FilesWritten == expectedRows ? "PASS" : "FAIL")}");

            writeQueue.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    private static void RunResumabilityTiming(string corpusRoot, int totalFiles, int childDirCount)
    {
        Console.WriteLine();
        Console.WriteLine("── Interrupt + resume ──");

        string dbPath = TempDbPath();
        try
        {
            var connectionFactory = new SqliteConnectionFactory(dbPath);
            Bootstrap(connectionFactory);
            long volumeId = InsertVolume(connectionFactory);

            var writeQueue = new WriteQueue(connectionFactory);
            var orchestrator = MakeOrchestrator(connectionFactory, writeQueue);
            var root = new PathCanonicalizer().CanonicalizeExisting(corpusRoot);

            int cancelAfter = childDirCount / 2;
            using var cts = new CancellationTokenSource();
            var progress = new SyncProgress(p =>
            {
                if (p.TopLevelChildrenCompleted >= cancelAfter)
                {
                    cts.Cancel();
                }
            });

            var swInterrupted = Stopwatch.StartNew();
            try
            {
                orchestrator.ScanAsync(volumeId, root, progress, cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            swInterrupted.Stop();

            long filesAfterInterrupt = CountFiles(connectionFactory);
            Console.WriteLine($"  interrupted after ~{cancelAfter}/{childDirCount} shards, {swInterrupted.Elapsed.TotalSeconds:F1}s, {filesAfterInterrupt:N0} rows indexed so far");

            var swResume = Stopwatch.StartNew();
            var resumeResult = orchestrator.ScanAsync(volumeId, root).GetAwaiter().GetResult();
            swResume.Stop();

            long filesAfterResume = CountFiles(connectionFactory);
            long expectedRows = totalFiles + childDirCount;

            Console.WriteLine($"  resume: {resumeResult.TopLevelChildrenCompletedThisRun} shard(s) processed in {swResume.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"  total after resume: {filesAfterResume:N0} rows ({expectedRows:N0} expected)");

            bool complete = filesAfterResume == expectedRows;
            bool resumeFasterThanFullRescan = resumeResult.TopLevelChildrenCompletedThisRun < childDirCount;
            Console.WriteLine($"  T16 resumability target: fully complete after resume [{(complete ? "PASS" : "FAIL")}], resume did not redo finished shards [{(resumeFasterThanFullRescan ? "PASS" : "FAIL")}]");

            writeQueue.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    private static ScanOrchestrator MakeOrchestrator(ISqliteConnectionFactory connectionFactory, IWriteQueue writeQueue)
    {
        var directoryEnumerator = new NtDirectoryEnumerator();
        return new ScanOrchestrator(
            directoryEnumerator,
            new WalkScanner(directoryEnumerator),
            new FileIndexWriter(writeQueue),
            new ScanCheckpointStore(connectionFactory, writeQueue),
            // The real DefaultSkipList() default excludes the OS Temp folder,
            // which is where this scenario's corpus lives — see
            // TRACKER.md's decision log for the same gotcha hit in tests.
            new DefaultSkipList(windowsRoot: @"C:\zara-scenario-unused-windows-root", tempRoot: @"C:\zara-scenario-unused-temp-root"));
    }

    private static void Bootstrap(ISqliteConnectionFactory connectionFactory)
    {
        using var connection = connectionFactory.CreateConnection();
        new MigrationRunner().MigrateToLatest(connection);
    }

    private static long InsertVolume(ISqliteConnectionFactory connectionFactory)
    {
        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO volumes (guid, serial, filesystem) VALUES ($guid, 1, 'NTFS'); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$guid", Guid.NewGuid().ToString());
        return (long)cmd.ExecuteScalar()!;
    }

    private static long CountFiles(ISqliteConnectionFactory connectionFactory)
    {
        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM files;";
        return (long)cmd.ExecuteScalar()!;
    }

    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"zara-scenario-scan-{Guid.NewGuid():N}.db");

    private static void CleanupDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        {
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    private sealed class SyncProgress(Action<ScanProgress> callback) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => callback(value);
    }
}
