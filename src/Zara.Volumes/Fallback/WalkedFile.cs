using Zara.Core.Files;

namespace Zara.Volumes.Fallback;

/// <summary>
/// One entry discovered by <see cref="WalkScanner"/>. Unlike
/// <c>RawDirectoryEntry</c> (which only carries a bare name — it doesn't know
/// its own parent), this carries the fully resolved <see cref="CanonicalPath"/>,
/// since a recursive walk is exactly what turns "name relative to some
/// directory" into "a real, addressable path".
/// </summary>
public readonly record struct WalkedFile(
    CanonicalPath Path,
    string Name,
    ulong? Frn,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    DateTimeOffset? AccessedUtc,
    FileAttributes Attributes,
    int Depth)
{
    public bool IsReparsePoint => Attributes.HasFlag(FileAttributes.ReparsePoint);
}
