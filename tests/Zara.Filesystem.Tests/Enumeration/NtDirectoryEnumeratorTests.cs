using Zara.Filesystem.Enumeration;
using Zara.Filesystem.Paths;

namespace Zara.Filesystem.Tests.Enumeration;

/// <summary>
/// Validates NtDirectoryEnumerator's NtQueryDirectoryFile-based parsing
/// against ground truth from .NET's own (FindFirstFileEx-based)
/// Directory.EnumerateFileSystemEntries. Real files, real syscalls — this is
/// exactly the kind of native-interop correctness that a struct-layout or
/// pointer-arithmetic mistake would silently corrupt, so it's tested against
/// disk rather than mocked.
/// </summary>
public class NtDirectoryEnumeratorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly NtDirectoryEnumerator _sut = new();
    private readonly PathCanonicalizer _canonicalizer = new();

    public NtDirectoryEnumeratorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-ntenum-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Enumerate_EmptyDirectory_YieldsNothing()
    {
        var results = _sut.Enumerate(Canonical(_tempRoot)).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void Enumerate_MatchesDotNetEnumerationByName()
    {
        CreateFiles(20, sizeBytes: 100);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "subdir-a"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "subdir-b"));

        var ntNames = _sut.Enumerate(Canonical(_tempRoot)).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var dotNetNames = Directory.EnumerateFileSystemEntries(_tempRoot)
            .Select(p => Path.GetFileName(p)!).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(dotNetNames, ntNames);
    }

    [Fact]
    public void Enumerate_NeverReturnsDotOrDotDot()
    {
        CreateFiles(3, sizeBytes: 10);

        var results = _sut.Enumerate(Canonical(_tempRoot)).ToList();

        Assert.DoesNotContain(results, e => e.Name is "." or "..");
    }

    [Fact]
    public void Enumerate_ReportsCorrectFileVsDirectory()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "a-file.txt"), "hi");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "a-dir"));

        var results = _sut.Enumerate(Canonical(_tempRoot)).ToDictionary(e => e.Name);

        Assert.False(results["a-file.txt"].IsDirectory);
        Assert.True(results["a-dir"].IsDirectory);
    }

    [Fact]
    public void Enumerate_ReportsCorrectFileSize()
    {
        byte[] content = new byte[12345];
        File.WriteAllBytes(Path.Combine(_tempRoot, "sized.bin"), content);

        var results = _sut.Enumerate(Canonical(_tempRoot)).ToList();
        var entry = Assert.Single(results);

        Assert.Equal(12345, entry.SizeBytes);
    }

    [Fact]
    public void Enumerate_ReportsPlausibleTimestamps()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        File.WriteAllText(Path.Combine(_tempRoot, "fresh.txt"), "hi");
        var after = DateTimeOffset.UtcNow.AddMinutes(1);

        var entry = _sut.Enumerate(Canonical(_tempRoot)).Single();

        Assert.NotNull(entry.ModifiedUtc);
        Assert.InRange(entry.ModifiedUtc!.Value, before, after);
        Assert.NotNull(entry.CreatedUtc);
        Assert.InRange(entry.CreatedUtc!.Value, before, after);
    }

    [Fact]
    public void Enumerate_EveryEntryHasAUniqueNonNullFrn()
    {
        CreateFiles(15, sizeBytes: 5);

        var results = _sut.Enumerate(Canonical(_tempRoot)).ToList();

        Assert.All(results, e => Assert.NotNull(e.Frn));
        Assert.Equal(results.Count, results.Select(e => e.Frn).Distinct().Count());
    }

    [Fact]
    public void Enumerate_HandlesMoreEntriesThanOneBufferBatch()
    {
        // 64 KB / a FILE_ID_BOTH_DIR_INFORMATION entry (~110 bytes fixed part
        // + name) comfortably holds a few hundred entries per call — this
        // forces at least two NtQueryDirectoryFile round-trips and proves the
        // restart-scan-then-continue loop doesn't drop or duplicate entries.
        const int count = 1500;
        CreateFiles(count, sizeBytes: 1);

        var results = _sut.Enumerate(Canonical(_tempRoot)).ToList();

        Assert.Equal(count, results.Count);
        Assert.Equal(count, results.Select(e => e.Name).Distinct().Count());
    }

    [Fact]
    public void Enumerate_NonexistentDirectory_Throws()
    {
        var missing = Path.Combine(_tempRoot, "does-not-exist");

        Assert.Throws<IOException>(() => _sut.Enumerate(
            Zara.Core.Files.CanonicalPath.FromCanonicalizedString(PathSyntax_ToExtendedLength(missing))).ToList());
    }

    private void CreateFiles(int count, int sizeBytes)
    {
        byte[] content = new byte[sizeBytes];
        for (int i = 0; i < count; i++)
        {
            File.WriteAllBytes(Path.Combine(_tempRoot, $"file-{i:D5}.dat"), content);
        }
    }

    private Zara.Core.Files.CanonicalPath Canonical(string path) => _canonicalizer.CanonicalizeExisting(path);

    // Local helper duplicating just enough of PathSyntax's prefixing logic for
    // the one "path doesn't exist" test above, without exposing the internal
    // type through this assembly's InternalsVisibleTo surface unnecessarily.
    private static string PathSyntax_ToExtendedLength(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path;
}
