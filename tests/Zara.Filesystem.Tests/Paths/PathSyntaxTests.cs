using Zara.Filesystem.Paths;

namespace Zara.Filesystem.Tests.Paths;

/// <summary>
/// Pure, no-I/O adversarial coverage for the string-level checks that back
/// PathValidator (ARCHITECTURE.md §27.1's "PATH" section). Kept separate from
/// PathValidatorTests so every case here runs in microseconds and the
/// integration-level tests can focus on cases that genuinely need real files.
/// </summary>
public class PathSyntaxTests
{
    // ── ToExtendedLength ────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Users\bob", @"\\?\C:\Users\bob")]
    [InlineData(@"C:\", @"\\?\C:\")]
    public void ToExtendedLength_PrefixesLocalPaths(string input, string expected) =>
        Assert.Equal(expected, PathSyntax.ToExtendedLength(input));

    [Fact]
    public void ToExtendedLength_PrefixesUncPaths() =>
        Assert.Equal(@"\\?\UNC\server\share\file", PathSyntax.ToExtendedLength(@"\\server\share\file"));

    [Fact]
    public void ToExtendedLength_IsIdempotent()
    {
        string once = PathSyntax.ToExtendedLength(@"C:\Users\bob");
        string twice = PathSyntax.ToExtendedLength(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void ToExtendedLength_LeavesAlreadyPrefixedPathAlone() =>
        Assert.Equal(@"\\?\C:\already\prefixed", PathSyntax.ToExtendedLength(@"\\?\C:\already\prefixed"));

    // ── IsDeviceNamespace ───────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\.\C:")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1")]
    [InlineData(@"\\?\GlobalRoot\Device\HarddiskVolume1")] // case-insensitive
    public void IsDeviceNamespace_DetectsDeviceAndGlobalrootPaths(string path) =>
        Assert.True(PathSyntax.IsDeviceNamespace(path));

    [Theory]
    [InlineData(@"C:\Users\bob\file.txt")]
    [InlineData(@"\\?\C:\Users\bob\file.txt")]
    [InlineData(@"\\server\share\file.txt")]
    public void IsDeviceNamespace_AllowsOrdinaryPaths(string path) =>
        Assert.False(PathSyntax.IsDeviceNamespace(path));

    // ── ContainsReservedDeviceName ──────────────────────────────────────────

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("CON.txt")]
    [InlineData(@"C:\Users\bob\CON")]
    [InlineData(@"C:\Users\bob\CON.txt")]
    [InlineData(@"C:\PRN\file.txt")] // reserved name as an intermediate directory
    [InlineData("NUL")]
    [InlineData("AUX")]
    [InlineData("COM1")]
    [InlineData("com9")]
    [InlineData("LPT1")]
    [InlineData("lpt9.log")]
    public void ContainsReservedDeviceName_DetectsReservedNames(string path) =>
        Assert.True(PathSyntax.ContainsReservedDeviceName(path));

    [Theory]
    [InlineData(@"C:\Users\bob\Console")]
    [InlineData(@"C:\Users\bob\Constant.txt")]
    [InlineData(@"C:\Users\bob\file.txt")]
    [InlineData(@"C:\Users\bob\COMPANY")]
    [InlineData(@"C:\Users\bob\LPT10")] // not a real reserved name
    public void ContainsReservedDeviceName_AllowsLookalikes(string path) =>
        Assert.False(PathSyntax.ContainsReservedDeviceName(path));

    // ── ContainsAlternateDataStream ─────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Users\bob\file.txt:hidden")]
    [InlineData(@"C:\Users\bob\file.txt:$DATA")]
    [InlineData(@"\\?\C:\Users\bob\file.txt:evil")]
    public void ContainsAlternateDataStream_DetectsAdsSyntax(string path) =>
        Assert.True(PathSyntax.ContainsAlternateDataStream(path));

    [Theory]
    [InlineData(@"C:\Users\bob\file.txt")]
    [InlineData(@"\\?\C:\Users\bob\file.txt")]
    [InlineData(@"\\?\UNC\server\share\file.txt")]
    [InlineData(@"D:\file.txt")]
    public void ContainsAlternateDataStream_AllowsOrdinaryDriveLetters(string path) =>
        Assert.False(PathSyntax.ContainsAlternateDataStream(path));

    // ── HasTrailingDotOrSpace ────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Users\bob\doc.txt.")]
    [InlineData(@"C:\Users\bob\doc.txt ")]
    [InlineData(@"C:\Users\bob\folder.")] // trailing-dot directory name
    public void HasTrailingDotOrSpace_DetectsTrailingDotOrSpace(string path) =>
        Assert.True(PathSyntax.HasTrailingDotOrSpace(path));

    [Theory]
    [InlineData(@"C:\Users\bob\doc.txt")]
    [InlineData(@"C:\Users\bob\.git")] // leading dot is fine, only trailing matters
    [InlineData(@"C:\Users\bob\")]     // trailing separator is stripped before the check
    public void HasTrailingDotOrSpace_AllowsOrdinaryPaths(string path) =>
        Assert.False(PathSyntax.HasTrailingDotOrSpace(path));

    // ── IsSameOrDescendant — the segment-boundary check ─────────────────────

    [Theory]
    [InlineData(@"C:\Users\bob", @"C:\Users\bob")]              // same path
    [InlineData(@"C:\Users\bob\", @"C:\Users\bob")]              // trailing slash on candidate
    [InlineData(@"C:\Users\bob\Documents\file.txt", @"C:\Users\bob")]
    [InlineData(@"C:\USERS\BOB\FILE.TXT", @"c:\users\bob")]      // case-insensitive
    public void IsSameOrDescendant_AcceptsTrueDescendants(string candidate, string root) =>
        Assert.True(PathSyntax.IsSameOrDescendant(candidate, root));

    [Theory]
    [InlineData(@"C:\Users\bobby", @"C:\Users\bob")]             // the classic StartsWith bug
    [InlineData(@"C:\Users\bob2\file.txt", @"C:\Users\bob")]
    [InlineData(@"C:\Users\alice", @"C:\Users\bob")]
    [InlineData(@"C:\Users", @"C:\Users\bob")]                   // ancestor is not a descendant
    [InlineData(@"D:\Users\bob\file.txt", @"C:\Users\bob")]      // different volume, same-looking suffix
    public void IsSameOrDescendant_RejectsLookalikesAndNonDescendants(string candidate, string root) =>
        Assert.False(PathSyntax.IsSameOrDescendant(candidate, root));
}
