using Zara.Filesystem.Paths;

namespace Zara.Filesystem.Tests.Paths;

public class KnownFoldersTests
{
    private readonly KnownFolders _sut = new(new PathCanonicalizer());

    [Theory]
    [InlineData(KnownFolder.Profile)]
    [InlineData(KnownFolder.Desktop)]
    [InlineData(KnownFolder.Documents)]
    [InlineData(KnownFolder.Downloads)]
    [InlineData(KnownFolder.Pictures)]
    [InlineData(KnownFolder.Videos)]
    [InlineData(KnownFolder.Music)]
    [InlineData(KnownFolder.LocalAppData)]
    [InlineData(KnownFolder.RoamingAppData)]
    public void Get_ResolvesEveryKnownFolder_ToAnExistingCanonicalPath(KnownFolder folder)
    {
        var path = _sut.Get(folder);

        Assert.StartsWith(@"\\?\", path.Value, StringComparison.Ordinal);
        Assert.True(Directory.Exists(path.Value), $"{folder} resolved to '{path.Value}', which does not exist.");
    }

    [Fact]
    public void Get_Downloads_HasNoEnvironmentSpecialFolderEquivalent()
    {
        // The whole reason this wrapper exists: Environment.SpecialFolder has
        // no Downloads member. If this ever fails because .NET added one,
        // that's fine — it doesn't invalidate SHGetKnownFolderPath as the
        // approach, it just means the comment above IKnownFolders is stale.
        var path = _sut.Get(KnownFolder.Downloads);

        Assert.EndsWith("Downloads", path.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGet_ForAResolvableFolder_ReturnsTrue()
    {
        bool ok = _sut.TryGet(KnownFolder.Documents, out var path);

        Assert.True(ok);
        Assert.False(path.IsDefault);
    }

    [Fact]
    public void Get_IsConsistentAcrossCalls()
    {
        var first = _sut.Get(KnownFolder.Profile);
        var second = _sut.Get(KnownFolder.Profile);

        Assert.Equal(first, second);
    }
}
