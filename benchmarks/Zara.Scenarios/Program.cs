using System.Diagnostics;
using Zara.Filesystem.Enumeration;
using Zara.Filesystem.Paths;
using Zara.Scenarios;

// Dispatch: `dotnet run -- list [fileCount]` (default) or `dotnet run -- scan
// [totalFiles] [childDirCount]`. Kept as a simple positional-arg dispatcher
// rather than a real CLI parser — this project has two scenarios, not twenty.
if (args.Length > 0 && string.Equals(args[0], "scan", StringComparison.OrdinalIgnoreCase))
{
    int scanTotalFiles = args.Length > 1 && int.TryParse(args[1], out int tf) ? tf : 100_000;
    int scanChildDirs = args.Length > 2 && int.TryParse(args[2], out int cd) ? cd : 20;
    ScanScenario.Run(scanTotalFiles, scanChildDirs);
    return 0;
}

// B12 (ARCHITECTURE.md §32.2): directory listing at scenario scale.
// T09's exit bar: 100k-file directory listing < 400ms, < 20MB allocated.
//
// Deliberately a plain Stopwatch/GC.GetAllocatedBytesForCurrentThread harness
// rather than BenchmarkDotNet: generating a 100k-file corpus and enumerating
// it is itself the expensive part (BenchmarkDotNet's process-isolation +
// pilot-stage overhead would multiply that cost several times over for
// little extra precision at this scale). BenchmarkDotNet lives in
// benchmarks/Zara.Benchmarks for micro-benchmarks of hot-path *methods*
// (e.g. the name index's trigram intersection at M3) where that precision
// earns its cost — see the decision log in TRACKER.md.

int fileCount = args.Length > 0 && int.TryParse(args[0], out int n) ? n : 100_000;
string corpusRoot = Path.Combine(Path.GetTempPath(), $"zara-scenario-b12-{fileCount}");

Console.WriteLine($"B12: directory listing, {fileCount:N0} files");
Console.WriteLine($"Corpus: {corpusRoot}");

EnsureCorpus(corpusRoot, fileCount);

var canonicalizer = new PathCanonicalizer();
var directory = canonicalizer.CanonicalizeExisting(corpusRoot);

RunScenario("NtDirectoryEnumerator (NtQueryDirectoryFile)", new NtDirectoryEnumerator(), directory, fileCount);
RunScenario("Win32DirectoryEnumerator (FindFirstFileEx fallback)", new Win32DirectoryEnumerator(), directory, fileCount);

return 0;

static void EnsureCorpus(string root, int count)
{
    if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Take(count).Count() == count)
    {
        Console.WriteLine("Corpus already present, reusing it.");
        return;
    }

    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }

    Directory.CreateDirectory(root);

    Console.WriteLine("Generating corpus...");
    var sw = Stopwatch.StartNew();
    byte[] content = new byte[64];
    for (int i = 0; i < count; i++)
    {
        File.WriteAllBytes(Path.Combine(root, $"file-{i:D6}.dat"), content);
    }
    sw.Stop();
    Console.WriteLine($"Corpus generated in {sw.Elapsed.TotalSeconds:F1}s ({count / sw.Elapsed.TotalSeconds:N0} files/s).");
}

static void RunScenario(string label, IDirectoryEnumerator enumerator, Zara.Core.Files.CanonicalPath directory, int expectedCount)
{
    Console.WriteLine();
    Console.WriteLine($"── {label} ──");

    // One untimed warmup pass: primes the OS file-metadata cache so every
    // enumerator is measured against the same warm-cache baseline, and pays
    // for JIT/tiered-compilation once outside the timed loop.
    _ = enumerator.Enumerate(directory).Count();

    const int iterations = 5;
    var durations = new List<TimeSpan>(iterations);
    var allocations = new List<long>(iterations);
    int lastCount = 0;

    for (int i = 0; i < iterations; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();

        lastCount = enumerator.Enumerate(directory).Count();

        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        durations.Add(sw.Elapsed);
        allocations.Add(allocAfter - allocBefore);
    }

    var sorted = durations.OrderBy(d => d).ToList();
    TimeSpan min = sorted[0];
    TimeSpan median = sorted[iterations / 2];
    TimeSpan max = sorted[^1];
    long avgAllocMb = (long)(allocations.Average() / (1024.0 * 1024.0) * 100) / 100;
    long minAllocBytes = allocations.Min();

    Console.WriteLine($"  entries returned : {lastCount:N0} (expected {expectedCount:N0})");
    Console.WriteLine($"  wall time        : min={min.TotalMilliseconds:F0}ms  median={median.TotalMilliseconds:F0}ms  max={max.TotalMilliseconds:F0}ms");
    Console.WriteLine($"  allocated        : min={minAllocBytes / (1024.0 * 1024.0):F1}MB  avg={avgAllocMb}MB");

    bool timePass = median.TotalMilliseconds < 400;
    bool allocPass = minAllocBytes < 20 * 1024 * 1024;
    bool countPass = lastCount == expectedCount;

    Console.WriteLine($"  T09 target       : median<400ms [{(timePass ? "PASS" : "FAIL")}]  alloc<20MB [{(allocPass ? "PASS" : "FAIL")}]  count correct [{(countPass ? "PASS" : "FAIL")}]");
}
