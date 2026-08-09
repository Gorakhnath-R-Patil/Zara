using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Zara.Core.Files;
using Zara.Search.Names;
using Zara.Search.Query;
using Zara.Storage;

namespace Zara.Scenarios;

/// <summary>
/// T21 (ARCHITECTURE.md tracker M3 / §32.2 B06-B07): name-search and
/// structured-query latency at scale. Synthetic names/rows generated
/// in-memory or via direct SQL insert — no real filesystem I/O needed here,
/// unlike B12/T16, since NameIndex and QueryPlanner both operate purely on
/// already-extracted metadata.
/// </summary>
internal static class SearchScenario
{
    public static void Run(int totalFiles)
    {
        Console.WriteLine($"T21: name search + structured query latency, {totalFiles:N0} synthetic files");
        RunNameIndexBenchmark(totalFiles);
        RunQueryPlannerBenchmark(totalFiles);
    }

    private static readonly string[] Patterns =
        ["document", "report", "invoice", "resume", "photo", "notes", "backup", "project", "budget", "screenshot"];

    private static readonly string[] Extensions = ["pdf", "docx", "jpg", "txt", "xlsx", "png", "zip"];

    private static void RunNameIndexBenchmark(int totalFiles)
    {
        Console.WriteLine();
        Console.WriteLine("── NameIndex (B06 target: p95 < 20ms) ──");

        var index = new NameIndex();
        var rng = new Random(42);

        var swBuild = Stopwatch.StartNew();
        for (int i = 0; i < totalFiles; i++)
        {
            string pattern = Patterns[rng.Next(Patterns.Length)];
            string ext = Extensions[rng.Next(Extensions.Length)];
            index.Upsert(new FileId(1, i), $"{pattern}_{i:D6}.{ext}");
        }
        swBuild.Stop();
        Console.WriteLine($"  build: {totalFiles:N0} entries in {swBuild.Elapsed.TotalSeconds:F1}s ({totalFiles / swBuild.Elapsed.TotalSeconds:N0} upserts/s)");

        // A representative mix: common-word prefix/substring queries (the
        // typical case) plus some numeric-suffix lookups (short/rare tokens).
        var queries = new List<string>();
        for (int i = 0; i < 300; i++)
        {
            queries.Add(Patterns[rng.Next(Patterns.Length)]);
        }
        for (int i = 0; i < 100; i++)
        {
            queries.Add(rng.Next(totalFiles).ToString("D6"));
        }

        foreach (string q in queries.Take(20))
        {
            index.Search(q); // warmup: JIT, not the OS file cache this time
        }

        var timings = new List<double>(queries.Count);
        foreach (string q in queries)
        {
            var sw = Stopwatch.StartNew();
            index.Search(q);
            sw.Stop();
            timings.Add(sw.Elapsed.TotalMilliseconds);
        }

        ReportPercentiles("name search", timings, targetP95Ms: 20);
    }

    private static void RunQueryPlannerBenchmark(int totalFiles)
    {
        Console.WriteLine();
        Console.WriteLine("── QueryPlanner + real SQLite (B07 target: p95 < 25ms) ──");

        string dbPath = Path.Combine(Path.GetTempPath(), $"zara-scenario-search-{Guid.NewGuid():N}.db");
        try
        {
            var connectionFactory = new SqliteConnectionFactory(dbPath);
            using (var connection = connectionFactory.CreateConnection())
            {
                new MigrationRunner().MigrateToLatest(connection);
                using var insertVolume = connection.CreateCommand();
                insertVolume.CommandText = "INSERT INTO volumes (id, guid, serial, filesystem) VALUES (1, 'v', 1, 'NTFS');";
                insertVolume.ExecuteNonQuery();
            }

            Console.WriteLine("Populating table...");
            var swBuild = Stopwatch.StartNew();
            PopulateFiles(connectionFactory, totalFiles);
            swBuild.Stop();
            Console.WriteLine($"  populated {totalFiles:N0} rows in {swBuild.Elapsed.TotalSeconds:F1}s");

            var planner = new QueryPlanner();
            var rng = new Random(42);
            var queryTexts = new List<string>();
            for (int i = 0; i < 200; i++)
            {
                queryTexts.Add($"ext:{Extensions[rng.Next(Extensions.Length)]} size:>{rng.Next(1, 1000)}kb");
            }

            using var readConnection = connectionFactory.CreateConnection();

            foreach (string q in queryTexts.Take(10))
            {
                RunOnce(readConnection, planner, q);
            }

            var timings = new List<double>(queryTexts.Count);
            foreach (string q in queryTexts)
            {
                var sw = Stopwatch.StartNew();
                RunOnce(readConnection, planner, q);
                sw.Stop();
                timings.Add(sw.Elapsed.TotalMilliseconds);
            }

            ReportPercentiles("structured query", timings, targetP95Ms: 25);
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    private static int RunOnce(SqliteConnection connection, QueryPlanner planner, string queryText)
    {
        var plan = planner.Plan(DslParser.Parse(queryText));
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM (SELECT id FROM files WHERE {plan.WhereClause} {plan.OrderByClause} LIMIT {plan.Limit});";
        foreach (var (key, value) in plan.Parameters)
        {
            cmd.Parameters.AddWithValue(key, value);
        }

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void PopulateFiles(ISqliteConnectionFactory connectionFactory, int totalFiles)
    {
        using var connection = connectionFactory.CreateConnection();
        var rng = new Random(42);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const int batchSize = 5000;

        for (int batchStart = 0; batchStart < totalFiles; batchStart += batchSize)
        {
            using var transaction = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO files (id, volume_id, frn, name, name_folded, ext, path_hash, depth, is_dir,
                                    size_bytes, created_utc, modified_utc, accessed_utc, attributes, indexed_utc)
                VALUES ($id, 1, $id, $name, $name, $ext, $id, 1, 0, $size, $t, $t, $t, 32, 0);
                """;
            var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
            var pName = cmd.Parameters.Add("$name", SqliteType.Text);
            var pExt = cmd.Parameters.Add("$ext", SqliteType.Text);
            var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
            var pT = cmd.Parameters.Add("$t", SqliteType.Integer);
            cmd.Prepare();

            int batchEnd = Math.Min(batchStart + batchSize, totalFiles);
            for (int i = batchStart; i < batchEnd; i++)
            {
                string ext = Extensions[rng.Next(Extensions.Length)];
                pId.Value = (long)i;
                pName.Value = $"file-{i:D6}.{ext}";
                pExt.Value = ext;
                pSize.Value = (long)rng.Next(1, 10_000_000);
                pT.Value = now - rng.Next(0, 400) * 86_400_000L;
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private static void ReportPercentiles(string label, List<double> timingsMs, double targetP95Ms)
    {
        var sorted = timingsMs.OrderBy(t => t).ToList();
        double p50 = Percentile(sorted, 0.50);
        double p95 = Percentile(sorted, 0.95);
        double p99 = Percentile(sorted, 0.99);

        Console.WriteLine($"  {label}: {sorted.Count:N0} queries — p50={p50:F2}ms p95={p95:F2}ms p99={p99:F2}ms");
        Console.WriteLine($"  T21 target: p95 < {targetP95Ms}ms [{(p95 < targetP95Ms ? "PASS" : "FAIL")}]");
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        int index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

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
}
