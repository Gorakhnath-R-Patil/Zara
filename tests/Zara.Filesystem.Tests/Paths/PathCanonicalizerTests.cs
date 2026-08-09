using Zara.Filesystem.Paths;

namespace Zara.Filesystem.Tests.Paths;

/// <summary>
/// Exercises PathCanonicalizer against the real filesystem. These tests create
/// and clean up their own temp directory per test run rather than touching
/// anything the developer cares about.
/// </summary>
public class PathCanonicalizerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly PathCanonicalizer _sut = new();

    public PathCanonicalizerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-test-" + Guid.NewGuid().ToString("N"));
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
            // Best-effort cleanup; a locked handle from a failed test run
            // shouldn't fail the whole suite.
        }
    }

    [Fact]
    public void CanonicalizeExisting_ResolvesADirectory()
    {
        var result = _sut.CanonicalizeExisting(_tempRoot);

        Assert.StartsWith(@"\\?\", result.Value, StringComparison.Ordinal);
        Assert.True(PathSyntax.IsSameOrDescendant(result.Value, PathSyntax.ToExtendedLength(_tempRoot)));
    }

    [Fact]
    public void CanonicalizeExisting_ResolvesAFile()
    {
        string file = Path.Combine(_tempRoot, "file.txt");
        File.WriteAllText(file, "hello");

        var result = _sut.CanonicalizeExisting(file);

        Assert.EndsWith("file.txt", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanonicalizeExisting_NormalizesCaseToWhatDiskActuallyHas()
    {
        string file = Path.Combine(_tempRoot, "MixedCase.txt");
        File.WriteAllText(file, "hello");

        // Ask with the wrong case; NTFS is case-preserving-but-insensitive, so
        // this must still open and resolve.
        string wrongCase = Path.Combine(_tempRoot, "mixedcase.TXT");

        var result = _sut.CanonicalizeExisting(wrongCase);

        Assert.EndsWith("MixedCase.txt", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalizeExisting_ThrowsForNonexistentPath()
    {
        string missing = Path.Combine(_tempRoot, "does-not-exist.txt");

        Assert.Throws<IOException>(() => _sut.CanonicalizeExisting(missing));
    }

    [Fact]
    public void CanonicalizeExisting_IsIdempotentOnItsOwnOutput()
    {
        var once = _sut.CanonicalizeExisting(_tempRoot);
        var twice = _sut.CanonicalizeExisting(once.Value);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void CanonicalizeProspective_DoesNotRequireThePathToExist()
    {
        string notYetCreated = Path.Combine(_tempRoot, "future-file.txt");

        var result = _sut.CanonicalizeProspective(notYetCreated);

        Assert.EndsWith("future-file.txt", result.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanonicalizeProspective_ResolvesDotDotSegmentsLexically()
    {
        string withTraversal = Path.Combine(_tempRoot, "a", "..", "b.txt");

        var result = _sut.CanonicalizeProspective(withTraversal);

        Assert.Equal(
            PathSyntax.ToExtendedLength(Path.Combine(_tempRoot, "b.txt")),
            result.Value,
            ignoreCase: true);
    }
}
