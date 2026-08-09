using Zara.Core.Files;

namespace Zara.Core.Tests.Files;

public class CanonicalPathTests
{
    [Theory]
    [InlineData(@"\\?\C:\Users\bob\file.txt", @"\\?\C:\USERS\BOB\FILE.TXT")]
    [InlineData(@"\\?\C:\a", @"\\?\c:\A")]
    public void Equals_IsOrdinalCaseInsensitive(string a, string b)
    {
        var left = CanonicalPath.FromCanonicalizedString(a);
        var right = CanonicalPath.FromCanonicalizedString(b);

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentPaths_AreNotEqual()
    {
        var a = CanonicalPath.FromCanonicalizedString(@"\\?\C:\a");
        var b = CanonicalPath.FromCanonicalizedString(@"\\?\C:\b");

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void FromCanonicalizedString_RejectsNullOrWhitespace()
    {
        Assert.Throws<ArgumentException>(() => CanonicalPath.FromCanonicalizedString(""));
        Assert.Throws<ArgumentException>(() => CanonicalPath.FromCanonicalizedString("   "));
    }

    [Fact]
    public void Default_IsDefault_AndAccessingValueThrows()
    {
        CanonicalPath path = default;

        Assert.True(path.IsDefault);
        Assert.Throws<InvalidOperationException>(() => path.Value);
    }

    [Fact]
    public void ToString_ReturnsTheUnderlyingValue()
    {
        var path = CanonicalPath.FromCanonicalizedString(@"\\?\C:\Users\bob");

        Assert.Equal(@"\\?\C:\Users\bob", path.ToString());
    }
}
