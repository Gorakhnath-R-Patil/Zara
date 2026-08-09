using Zara.Core.Files;

namespace Zara.Filesystem.Hashing;

/// <summary>
/// The confirmation hash for duplicate detection — ARCHITECTURE.md §7/§11.6:
/// BLAKE3 over the full file content. Only run this on files that already
/// share a size and an <see cref="IQuickHasher"/> result; running it as a
/// first pass over an entire volume would mean reading every byte of every
/// file, which is exactly the cost the quick hash exists to avoid.
/// </summary>
public interface IContentHasher
{
    /// <summary>Lowercase hex-encoded BLAKE3 digest of the file's full contents.</summary>
    string ComputeHex(CanonicalPath path);
}
