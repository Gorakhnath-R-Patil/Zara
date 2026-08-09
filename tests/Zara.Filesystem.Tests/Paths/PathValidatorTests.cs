using Zara.Core.Files;
using Zara.Filesystem.Paths;

namespace Zara.Filesystem.Tests.Paths;

/// <summary>
/// Integration-level adversarial coverage for PathValidator against a real
/// temp filesystem — this is the executable form of ARCHITECTURE.md §27.1's
/// PATH test matrix. Pure syntax-only cases (no real files needed) live in
/// <see cref="PathSyntaxTests"/>; everything here needs an actual disk
/// structure to prove the segment-boundary and existence logic end to end.
/// </summary>
public class PathValidatorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _bobRoot;   // the allowed root for most tests
    private readonly string _bobbyDir;  // a sibling that must NOT match "bob" as a prefix
    private readonly PathValidator _sut;
    private readonly IReadOnlyList<CanonicalPath> _allowedRoots;

    public PathValidatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-validator-test-" + Guid.NewGuid().ToString("N"));
        _bobRoot = Path.Combine(_tempRoot, "bob");
        _bobbyDir = Path.Combine(_tempRoot, "bobby");
        Directory.CreateDirectory(_bobRoot);
        Directory.CreateDirectory(_bobbyDir);

        var canonicalizer = new PathCanonicalizer();
        _sut = new PathValidator(canonicalizer);
        _allowedRoots = [canonicalizer.CanonicalizeExisting(_bobRoot)];
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

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_PathInsideAllowedRoot_IsValid()
    {
        string file = Path.Combine(_bobRoot, "doc.txt");
        File.WriteAllText(file, "hi");

        var result = _sut.Validate(file, PathPurpose.Read, _allowedRoots);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_TheRootItself_IsValid()
    {
        var result = _sut.Validate(_bobRoot, PathPurpose.Enumerate, _allowedRoots);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyAllowedRootsList_MeansNoRestriction()
    {
        string file = Path.Combine(_bobbyDir, "anything.txt");
        File.WriteAllText(file, "hi");

        var result = _sut.Validate(file, PathPurpose.Read, []);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WritePurpose_AllowsPathThatDoesNotExistYet()
    {
        string notYetCreated = Path.Combine(_bobRoot, "new-file.txt");

        var result = _sut.Validate(notYetCreated, PathPurpose.Write, _allowedRoots);

        Assert.True(result.IsValid);
    }

    // ── The classic StartsWith bug ──────────────────────────────────────────

    [Fact]
    public void Validate_SiblingDirectoryWithPrefixedName_IsRejected()
    {
        // "bobby" is not a descendant of "bob" — a naive StartsWith("...\bob")
        // would wrongly accept it because "bobby" starts with "bob".
        string file = Path.Combine(_bobbyDir, "file.txt");
        File.WriteAllText(file, "hi");

        var result = _sut.Validate(file, PathPurpose.Read, _allowedRoots);

        Assert.False(result.IsValid);
        Assert.Contains("outside", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Path traversal escaping the allowed root ────────────────────────────

    [Fact]
    public void Validate_DotDotTraversalEscapingRoot_IsRejected()
    {
        // bob/../bobby/file.txt lexically resolves outside the allowed root.
        string file = Path.Combine(_bobRoot, "..", "bobby", "file.txt");
        File.WriteAllText(Path.Combine(_bobbyDir, "file.txt"), "hi");

        var result = _sut.Validate(file, PathPurpose.Read, _allowedRoots);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_DeepDotDotTraversal_ResolvesAndIsCheckedAgainstRoot()
    {
        // A long ".." chain that ultimately lands back inside the allowed root
        // must be ACCEPTED — traversal itself isn't the crime, escaping the
        // root is. This guards against an overly-blunt "reject any .." rule.
        string file = Path.Combine(_bobRoot, "a", "b", "..", "..", "doc.txt");
        File.WriteAllText(Path.Combine(_bobRoot, "doc.txt"), "hi");

        var result = _sut.Validate(file, PathPurpose.Read, _allowedRoots);

        Assert.True(result.IsValid);
    }

    // ── Device namespaces, reserved names, ADS, trailing dot/space ──────────

    [Theory]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\.\C:")]
    public void Validate_DeviceNamespacePath_IsRejected(string devicePath)
    {
        var result = _sut.Validate(devicePath, PathPurpose.Read, []);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_ReservedDeviceName_IsRejected()
    {
        var result = _sut.Validate("CON", PathPurpose.Read, []);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_AlternateDataStreamSyntax_IsRejected()
    {
        string file = Path.Combine(_bobRoot, "doc.txt");
        File.WriteAllText(file, "hi");

        var result = _sut.Validate(file + ":hidden", PathPurpose.Read, _allowedRoots);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_TrailingDotOnFinalSegment_IsRejected()
    {
        string withTrailingDot = Path.Combine(_bobRoot, "doc.txt.");

        var result = _sut.Validate(withTrailingDot, PathPurpose.Write, _allowedRoots);

        Assert.False(result.IsValid);
    }

    // ── Length and existence edge cases ─────────────────────────────────────

    [Fact]
    public void Validate_VeryLongPath_IsHandledNotThrown()
    {
        // A 2000-character path is well under our hard ceiling but almost
        // certainly doesn't exist — it must come back as an ordinary Invalid
        // result, never an unhandled exception.
        string longSegment = new string('a', 2000);
        string longPath = Path.Combine(_bobRoot, longSegment);

        var exception = Record.Exception(() => _sut.Validate(longPath, PathPurpose.Read, _allowedRoots));

        Assert.Null(exception);
        Assert.False(_sut.Validate(longPath, PathPurpose.Read, _allowedRoots).IsValid);
    }

    [Fact]
    public void Validate_ExtremelyLongPath_ExceedingHardCeiling_IsRejectedWithoutThrowing()
    {
        string huge = Path.Combine(_bobRoot, new string('a', 40_000));

        var exception = Record.Exception(() => _sut.Validate(huge, PathPurpose.Read, _allowedRoots));

        Assert.Null(exception);
        Assert.False(_sut.Validate(huge, PathPurpose.Read, _allowedRoots).IsValid);
    }

    [Fact]
    public void Validate_NonexistentPathForReadPurpose_IsRejected()
    {
        string missing = Path.Combine(_bobRoot, "does-not-exist.txt");

        var result = _sut.Validate(missing, PathPurpose.Read, _allowedRoots);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyString_IsRejectedNotThrown()
    {
        var result = _sut.Validate("", PathPurpose.Read, _allowedRoots);

        Assert.False(result.IsValid);
    }
}
