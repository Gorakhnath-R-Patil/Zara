using Zara.Filesystem.Enumeration;
using Zara.Filesystem.Paths;

namespace Zara.Filesystem.Tests.Enumeration;

public class Win32DirectoryEnumeratorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly Win32DirectoryEnumerator _sut = new();
    private readonly PathCanonicalizer _canonicalizer = new();

    public Win32DirectoryEnumeratorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-win32enum-test-" + Guid.NewGuid().ToString("N"));
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
    public void Enumerate_EmptyDirectory_YieldsNothing() =>
        Assert.Empty(_sut.Enumerate(Canonical(_tempRoot)));

    [Fact]
    public void Enumerate_MatchesDotNetEnumerationByName()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "a.txt"), "hi");
        File.WriteAllText(Path.Combine(_tempRoot, "b.txt"), "hi");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "sub"));

        var names = _sut.Enumerate(Canonical(_tempRoot)).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(["a.txt", "b.txt", "sub"], names);
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
        File.WriteAllBytes(Path.Combine(_tempRoot, "sized.bin"), new byte[777]);

        var entry = _sut.Enumerate(Canonical(_tempRoot)).Single();

        Assert.Equal(777, entry.SizeBytes);
    }

    [Fact]
    public void Enumerate_NeverProvidesAFrn()
    {
        // Documented behavior, not a bug: getting the FRN here would cost a
        // second syscall per entry and defeat the point of a fast fallback —
        // see the class-level remarks on Win32DirectoryEnumerator.
        File.WriteAllText(Path.Combine(_tempRoot, "a.txt"), "hi");

        var entry = _sut.Enumerate(Canonical(_tempRoot)).Single();

        Assert.Null(entry.Frn);
    }

    [Fact]
    public void Enumerate_NonexistentDirectory_ThrowsIOException()
    {
        var missing = Zara.Core.Files.CanonicalPath.FromCanonicalizedString(
            @"\\?\" + Path.Combine(_tempRoot, "does-not-exist"));

        Assert.Throws<IOException>(() => _sut.Enumerate(missing).ToList());
    }

    private Zara.Core.Files.CanonicalPath Canonical(string path) => _canonicalizer.CanonicalizeExisting(path);
}
