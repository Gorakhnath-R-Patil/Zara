namespace Zara.Core.Files;

/// <summary>
/// A single filesystem entry as surfaced to the rest of the system — the common
/// shape returned by both live enumeration (<c>IFileSystem</c>) and index queries
/// (<c>ISearchEngine</c>), so callers don't need to distinguish "from disk" vs.
/// "from the index" until they actually care.
/// </summary>
public sealed record FileEntry(
    FileId Id,
    CanonicalPath Path,
    string Name,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    DateTimeOffset? AccessedUtc,
    FileAttributes Attributes)
{
    /// <summary>Lowercase extension without the leading dot, or null for directories
    /// and extensionless files.</summary>
    public string? Extension => IsDirectory ? null : GetExtension(Name);

    public bool IsReparsePoint => Attributes.HasFlag(FileAttributes.ReparsePoint);
    public bool IsHidden => Attributes.HasFlag(FileAttributes.Hidden);
    public bool IsSystem => Attributes.HasFlag(FileAttributes.System);

    /// <summary>
    /// True when the file is a cloud placeholder that has not been downloaded
    /// (OneDrive/Dropbox "online-only" state). Reading such a file's contents
    /// triggers a download — see ARCHITECTURE.md §10.5. Callers must check this
    /// before opening a file for content extraction.
    /// </summary>
    public bool IsCloudPlaceholder =>
        Attributes.HasFlag(FileAttributes.Offline) ||
        ((int)Attributes & RecallOnDataAccess) != 0;

    // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000; not in System.IO.FileAttributes.
    private const int RecallOnDataAccess = 0x00400000;

    private static string? GetExtension(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..].ToLowerInvariant() : null;
    }
}
