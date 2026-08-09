using Zara.Filesystem.Hashing;
using Zara.Filesystem.Paths;

namespace Zara.Filesystem.Tests.Hashing;

public class QuickHasherTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly PathCanonicalizer _canonicalizer = new();
    private readonly QuickHasher _sut = new();

    public QuickHasherTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-quickhash-test-" + Guid.NewGuid().ToString("N"));
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
    public void Compute_IdenticalFiles_ProduceTheSameHash()
    {
        byte[] content = "hello world"u8.ToArray();
        var a = WriteFile("a.txt", content);
        var b = WriteFile("b.txt", content);

        Assert.Equal(_sut.Compute(a), _sut.Compute(b));
    }

    [Fact]
    public void Compute_DifferentSizes_ProduceDifferentHashes()
    {
        var a = WriteFile("a.txt", "short"u8.ToArray());
        var b = WriteFile("b.txt", "a much longer piece of content here"u8.ToArray());

        Assert.NotEqual(_sut.Compute(a), _sut.Compute(b));
    }

    [Fact]
    public void Compute_SameSizeDifferentContent_ProducesDifferentHashes()
    {
        var a = WriteFile("a.txt", "aaaaaaaaaa"u8.ToArray());
        var b = WriteFile("b.txt", "bbbbbbbbbb"u8.ToArray());

        Assert.NotEqual(_sut.Compute(a), _sut.Compute(b));
    }

    [Fact]
    public void Compute_EmptyFile_DoesNotThrow()
    {
        var a = WriteFile("empty.txt", []);

        var exception = Record.Exception(() => _sut.Compute(a));

        Assert.Null(exception);
    }

    [Fact]
    public void Compute_FileSmallerThanOneChunk_IsDeterministic()
    {
        var a = WriteFile("small.txt", "just a few bytes"u8.ToArray());

        long first = _sut.Compute(a);
        long second = _sut.Compute(a);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_FileLargerThanTwoChunks_OnlyMiddleChangeIsInvisible()
    {
        // The quick hash only samples the first and last 4KB — a change
        // purely in the MIDDLE of a large file (beyond both sampled
        // windows) is expected to be invisible to it. This is a documented
        // characteristic, not a bug: it's exactly why quick_hash matches are
        // only CANDIDATE duplicates, always confirmed by a full content hash.
        byte[] original = new byte[20_000];
        new Random(1).NextBytes(original);
        byte[] modifiedMiddle = (byte[])original.Clone();
        modifiedMiddle[10_000] ^= 0xFF; // flip a byte well inside the unsampled middle

        var a = WriteFile("a.bin", original);
        var b = WriteFile("b.bin", modifiedMiddle);

        Assert.Equal(_sut.Compute(a), _sut.Compute(b));
    }

    [Fact]
    public void Compute_ChangeInHead_ProducesDifferentHash()
    {
        byte[] original = new byte[20_000];
        new Random(2).NextBytes(original);
        byte[] modifiedHead = (byte[])original.Clone();
        modifiedHead[0] ^= 0xFF;

        var a = WriteFile("a.bin", original);
        var b = WriteFile("b.bin", modifiedHead);

        Assert.NotEqual(_sut.Compute(a), _sut.Compute(b));
    }

    [Fact]
    public void Compute_ChangeInTail_ProducesDifferentHash()
    {
        byte[] original = new byte[20_000];
        new Random(3).NextBytes(original);
        byte[] modifiedTail = (byte[])original.Clone();
        modifiedTail[^1] ^= 0xFF;

        var a = WriteFile("a.bin", original);
        var b = WriteFile("b.bin", modifiedTail);

        Assert.NotEqual(_sut.Compute(a), _sut.Compute(b));
    }
}
