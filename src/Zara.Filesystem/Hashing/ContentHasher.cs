using Blake3;
using Zara.Core.Files;

namespace Zara.Filesystem.Hashing;

/// <inheritdoc cref="IContentHasher"/>
public sealed class ContentHasher : IContentHasher
{
    private const int BufferSize = 81_920;

    public string ComputeHex(CanonicalPath path)
    {
        using var stream = new FileStream(path.Value, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var hasher = Hasher.New();

        Span<byte> buffer = new byte[BufferSize];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            hasher.Update(buffer[..read]);
        }

        Hash hash = hasher.Finalize();
        return hash.ToString();
    }
}
