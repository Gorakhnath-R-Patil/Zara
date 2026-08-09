namespace Zara.Filesystem.Enumeration;

/// <summary>
/// One entry from a raw directory enumeration. Deliberately NOT a full
/// <see cref="Zara.Core.Files.FileEntry"/>: that type's <c>FileId</c> needs a
/// resolved volume-table surrogate key, which is Zara.Storage's job (added at
/// M2+), not this layer's — see ARCHITECTURE.md §8.2's module boundary table.
/// Callers that need a full <c>FileEntry</c> combine this with volume context
/// they already have.
/// </summary>
/// <param name="Frn">The NTFS File Reference Number, when the enumerator used to
/// produce this entry can supply one cheaply (only <see cref="NtDirectoryEnumerator"/>
/// can — see its remarks). Null from the FindFirstFileEx-based fallback, where
/// getting it would cost a second syscall per entry and defeat the point of
/// having a fallback at all.</param>
public readonly record struct RawDirectoryEntry(
    ulong? Frn,
    string Name,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    DateTimeOffset? AccessedUtc,
    FileAttributes Attributes);
