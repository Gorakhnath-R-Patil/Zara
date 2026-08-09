using Zara.Volumes.Skip;

namespace Zara.Volumes.Tests.Skip;

public class DefaultSkipListTests
{
    private readonly DefaultSkipList _sut = new(windowsRoot: @"C:\Windows", tempRoot: @"C:\Users\bob\AppData\Local\Temp");

    [Theory]
    [InlineData(@"C:\Projects\app\node_modules", true)]
    [InlineData(@"C:\Projects\app\node_modules\lodash\index.js", false)]
    [InlineData(@"C:\repo\.git\objects\ab\cdef1234", false)]
    [InlineData(@"C:\repo\.git\objects", true)]
    [InlineData(@"C:\repo\target\debug\build", true)]
    [InlineData(@"C:\repo\bin\Debug\net9.0", true)]
    [InlineData(@"C:\repo\.venv\Lib", true)]
    [InlineData(@"C:\repo\__pycache__", true)]
    [InlineData(@"C:\repo\.gradle\caches", true)]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21", true)]
    [InlineData(@"C:\System Volume Information\tracking.log", false)]
    [InlineData(@"C:\$Extend\$ObjId", true)]
    public void ShouldSkip_MatchesTheDocumentedHardExclusions(string path, bool isDirectory)
    {
        Assert.True(_sut.ShouldSkip(path, isDirectory));
    }

    [Theory]
    [InlineData(@"C:\repo\.git\config", false)]   // .git itself is NOT excluded, only .git\objects
    [InlineData(@"C:\repo\bin\Release\net9.0", true)] // "bin\Release", not "bin\Debug"
    [InlineData(@"C:\repo\target\release", true)]     // "target\release", not "target\debug"
    [InlineData(@"C:\Projects\my-node_modules-tool.exe", false)] // filename contains the substring, not a real segment
    [InlineData(@"C:\Users\bob\Documents\report.pdf", false)]
    public void ShouldSkip_DoesNotOverMatch(string path, bool isDirectory)
    {
        Assert.False(_sut.ShouldSkip(path, isDirectory));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\drivers", true)]
    [InlineData(@"C:\Windows\explorer.exe", false)]
    public void ShouldSkip_ExcludesWindowsDirectory(string path, bool isDirectory)
    {
        Assert.True(_sut.ShouldSkip(path, isDirectory));
    }

    [Theory]
    [InlineData(@"C:\Windows\Fonts", true)]
    [InlineData(@"C:\Windows\Fonts\arial.ttf", false)]
    public void ShouldSkip_MakesAnExceptionForWindowsFonts(string path, bool isDirectory)
    {
        Assert.False(_sut.ShouldSkip(path, isDirectory));
    }

    [Fact]
    public void ShouldSkip_ExcludesTheUsersTempDirectory()
    {
        Assert.True(_sut.ShouldSkip(@"C:\Users\bob\AppData\Local\Temp\some-installer.tmp", false));
        Assert.True(_sut.ShouldSkip(@"C:\Users\bob\AppData\Local\Temp\sub\file.txt", false));
    }

    [Theory]
    [InlineData("pagefile.sys")]
    [InlineData("hiberfil.sys")]
    [InlineData("swapfile.sys")]
    [InlineData("DumpStack.log.tmp")]
    public void ShouldSkip_ExcludesSystemFiles(string fileName)
    {
        Assert.True(_sut.ShouldSkip($@"C:\{fileName}", isDirectory: false));
    }

    [Fact]
    public void ShouldSkip_ExtendedLengthPrefixedPaths_AreHandledTheSameAsPlainPaths()
    {
        Assert.True(_sut.ShouldSkip(@"\\?\C:\repo\node_modules", true));
        Assert.False(_sut.ShouldSkip(@"\\?\C:\Users\bob\Documents\report.pdf", false));
    }
}
