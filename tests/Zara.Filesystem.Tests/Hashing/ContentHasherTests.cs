using Zara.Filesystem.Hashing;
using Zara.Filesystem.Paths;

namespace Zara.Filesystem.Tests.Hashing;

public class ContentHasherTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly PathCanonicalizer _canonicalizer = new();
    private readonly ContentHasher _sut = new();

    public ContentHasherTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-contenthash-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch (IOException) { }
    }

    private Zara.Core.Files.CanonicalPath WriteFile(string name, byte[] content)
    {
        string path = Path.Combine(_tempRoot, name);
        File.WriteAllBytes(path, content);
        return _canonicalizer.CanonicalizeExisting(path);
    }

    [Fact]
    public void ComputeHex_IdenticalContent_ProducesTheSameHash()
    {
        byte[] content = "hello world"u8.ToArray();
        var a = WriteFile("a.txt", content);
        var b = WriteFile("b.txt", content);

        Assert.Equal(_sut.ComputeHex(a), _sut.ComputeHex(b));
    }

    [Fact]
    public void ComputeHex_DifferentContent_ProducesDifferentHashes()
    {
        var a = WriteFile("a.txt", "content A"u8.ToArray());
        var b = WriteFile("b.txt", "content B"u8.ToArray());

        Assert.NotEqual(_sut.ComputeHex(a), _sut.ComputeHex(b));
    }

    [Fact]
    public void ComputeHex_DetectsAChangeAnywhereInTheFile_UnlikeQuickHash()
    {
        // Unlike QuickHasher, this MUST detect a change purely in the middle
        // of a large file — that's the entire reason it exists as the
        // confirmation step.
        byte[] original = new byte[20_000];
        new Random(1).NextBytes(original);
        byte[] modifiedMiddle = (byte[])original.Clone();
        modifiedMiddle[10_000] ^= 0xFF;

        var a = WriteFile("a.bin", original);
        var b = WriteFile("b.bin", modifiedMiddle);

        Assert.NotEqual(_sut.ComputeHex(a), _sut.ComputeHex(b));
    }

    [Fact]
    public void ComputeHex_EmptyFile_DoesNotThrow()
    {
        var a = WriteFile("empty.txt", []);

        var exception = Record.Exception(() => _sut.ComputeHex(a));

        Assert.Null(exception);
    }

    [Fact]
    public void ComputeHex_IsStableAcrossCalls()
    {
        var a = WriteFile("a.txt", "stable content"u8.ToArray());

        Assert.Equal(_sut.ComputeHex(a), _sut.ComputeHex(a));
    }

    [Fact]
    public void ComputeHex_LargerThanOneBufferChunk_StillHashesTheWholeFile()
    {
        // Bigger than ContentHasher's internal 80KB read buffer, to prove
        // the streaming loop actually covers the whole file, not just the
        // first chunk.
        byte[] content = new byte[300_000];
        new Random(4).NextBytes(content);
        byte[] modifiedNearEnd = (byte[])content.Clone();
        modifiedNearEnd[^1] ^= 0xFF;

        var a = WriteFile("a.bin", content);
        var b = WriteFile("b.bin", modifiedNearEnd);

        Assert.NotEqual(_sut.ComputeHex(a), _sut.ComputeHex(b));
    }

    [Fact]
    public void ComputeHex_ReturnsLowercaseHex()
    {
        var a = WriteFile("a.txt", "x"u8.ToArray());

        string hash = _sut.ComputeHex(a);

        Assert.Matches("^[0-9a-f]+$", hash);
    }
}
