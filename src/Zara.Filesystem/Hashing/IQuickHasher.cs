using Zara.Core.Files;

namespace Zara.Filesystem.Hashing;

/// <summary>
/// A fast, non-cryptographic pre-filter hash — ARCHITECTURE.md §11.6:
/// <c>quick_hash: xxHash3(size + 4KB head + 4KB tail)</c>. Two files with the
/// same quick hash are CANDIDATE duplicates, not confirmed ones — always
/// verify with <see cref="IContentHasher"/> before treating them as
/// identical. The point of this hash is that computing it costs at most
/// 8KB of I/O regardless of file size, so it can run over an entire index
/// before paying for a single full-file read.
/// </summary>
public interface IQuickHasher
{
    long Compute(CanonicalPath path);
}
