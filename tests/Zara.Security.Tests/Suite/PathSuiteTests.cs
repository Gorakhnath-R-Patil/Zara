using Zara.Core.Files;
using Zara.Filesystem.Paths;

namespace Zara.Security.Tests.Suite;

/// <summary>
/// T49 / ARCHITECTURE.md §27.1's PATH suite, matched case-for-case against
/// the documented list rather than PathValidatorTests' broader adversarial
/// coverage (T05) — this file exists specifically so §27.1's list has one
/// traceable test per line, for whoever next reads the architecture doc
/// against what's actually verified. Runs against the real, live
/// <see cref="PathValidator"/>/<see cref="PathCanonicalizer"/>, on real
/// temp files, same as every other path test this session.
/// </summary>
public class PathSuiteTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly PathCanonicalizer _canonicalizer = new();
    private readonly PathValidator _sut;

    public PathSuiteTests()
    {
        _sut = new PathValidator(_canonicalizer);
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-pathsuite-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "bob"));
        File.WriteAllText(Path.Combine(_tempRoot, "bob", "doc.txt"), "hi");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch (IOException) { }
    }

    private CanonicalPath AllowedRoot() => _canonicalizer.CanonicalizeExisting(Path.Combine(_tempRoot, "bob"));

    [Fact]
    public void RelativeTraversal_EscapingTheAllowedRoot_IsBlocked()
    {
        string traversal = Path.Combine(_tempRoot, "bob", "..", "..", "..", "Windows", "System32");

        var result = _sut.Validate(traversal, PathPurpose.Read, [AllowedRoot()]);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AbsoluteTraversal_ResolvingOutsideTheAllowedRoot_IsBlocked()
    {
        string traversal = Path.Combine(_tempRoot, "bob", "..", "..") + Path.DirectorySeparatorChar;

        var result = _sut.Validate(traversal, PathPurpose.Enumerate, [AllowedRoot()]);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ExtendedLengthPrefixedPathOutsideTheAllowedRoot_IsBlocked()
    {
        string outsideThePath = @"\\?\C:\Windows\System32";

        var result = _sut.Validate(outsideThePath, PathPurpose.Read, [AllowedRoot()]);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void SiblingWithAPrefixedName_IsBlocked_SegmentComparisonNotStartsWith()
    {
        // "bobby" must not match an allowed root of "...\bob" via a naive
        // StartsWith — §17.3 item 6's exact concern.
        Directory.CreateDirectory(Path.Combine(_tempRoot, "bobby"));
        string sibling = Path.Combine(_tempRoot, "bobby");

        var result = _sut.Validate(sibling, PathPurpose.Enumerate, [AllowedRoot()]);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AlternateDataStreamSyntax_IsBlocked()
    {
        string ads = Path.Combine(_tempRoot, "bob", "doc.txt:evil");

        var result = _sut.Validate(ads, PathPurpose.Read, []);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void TrailingDot_IsBlocked()
    {
        string trailingDot = Path.Combine(_tempRoot, "bob", "doc.txt.");

        var result = _sut.Validate(trailingDot, PathPurpose.Read, []);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\.\C:")]
    public void ReservedDeviceNamesAndDeviceNamespaces_AreBlocked(string devicePath)
    {
        var result = _sut.Validate(devicePath, PathPurpose.Read, []);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void VeryLongPath_IsHandledGracefully_NotCrashed()
    {
        // Under PathValidator's hard 32,000-char ceiling but far longer than
        // any real filename — the property under test is "returns a
        // structured Invalid result", not "throws".
        string longSegment = new string('a', 2000);
        string longPath = Path.Combine(_tempRoot, "bob", longSegment + ".txt");

        var exception = Record.Exception(() => _sut.Validate(longPath, PathPurpose.Read, []));
        var result = _sut.Validate(longPath, PathPurpose.Read, []);

        Assert.Null(exception);
        Assert.False(result.IsValid); // doesn't exist on disk — expected to fail validation, just not crash
    }

    [Fact]
    public void PathOverTheHardLengthCeiling_IsRejectedGracefully_NotCrashed()
    {
        string massivePath = @"C:\" + new string('a', 33_000);

        var exception = Record.Exception(() => _sut.Validate(massivePath, PathPurpose.Read, []));
        var result = _sut.Validate(massivePath, PathPurpose.Read, []);

        Assert.Null(exception);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void UnicodeRtlOverrideInFilename_IsHandledGracefully_NotCrashed()
    {
        // U+202E RIGHT-TO-LEFT OVERRIDE — the classic "cod.exe" ->
        // displays-as-"exe.doc" filename spoofing trick. Display-escaping it
        // is a UI concern (M5); what's tested here is that validation itself
        // doesn't throw or misbehave on it.
        string rtlName = "safe\u202Egnp.exe"; // displays misleadingly; validated as raw bytes regardless
        string rtlPath = Path.Combine(_tempRoot, "bob", rtlName);

        var exception = Record.Exception(() => _sut.Validate(rtlPath, PathPurpose.Read, []));

        Assert.Null(exception);
    }
}
