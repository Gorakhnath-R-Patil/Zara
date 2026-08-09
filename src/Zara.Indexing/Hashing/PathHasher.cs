using System.IO.Hashing;
using System.Text;

namespace Zara.Indexing.Hashing;

/// <summary>
/// Computes the <c>files.path_hash</c> column (ARCHITECTURE.md §22) — a fast,
/// non-cryptographic fingerprint used for pre-filtering, not for security
/// decisions. Hashes the lowercase-invariant form so the hash agrees with
/// NTFS's own case-insensitive identity: two paths differing only in case
/// must hash identically.
/// </summary>
public static class PathHasher
{
    public static long Compute(string canonicalPath)
    {
        ArgumentNullException.ThrowIfNull(canonicalPath);

        byte[] bytes = Encoding.UTF8.GetBytes(canonicalPath.ToUpperInvariant());
        ulong hash = XxHash3.HashToUInt64(bytes);
        return unchecked((long)hash);
    }
}
