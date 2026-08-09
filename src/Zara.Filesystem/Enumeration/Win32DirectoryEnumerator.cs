using Zara.Core.Files;

namespace Zara.Filesystem.Enumeration;

/// <summary>
/// The universal fallback enumerator for volumes/paths where
/// <see cref="NtDirectoryEnumerator"/> doesn't apply — non-NTFS filesystems,
/// paths <c>NtQueryDirectoryFile</c> is denied on, or (in a later phase)
/// network shares. ARCHITECTURE.md §10.2 specifies this path is
/// <c>FindFirstFileEx</c>-based; rather than hand-rolling that P/Invoke
/// surface, this wraps <see cref="DirectoryInfo.EnumerateFileSystemInfos()"/>,
/// which the .NET runtime itself implements on top of
/// <c>FindFirstFileEx(FIND_FIRST_EX_LARGE_FETCH)</c> and populates without
/// extra per-entry syscalls. That gets the "works everywhere, no elevation,
/// no undocumented API" property the fallback exists for, at the cost of the
/// one thing this path was never going to have cheaply anyway: the FRN
/// (see <see cref="RawDirectoryEntry.Frn"/> — always null from here).
/// </summary>
public sealed class Win32DirectoryEnumerator : IDirectoryEnumerator
{
    public IEnumerable<RawDirectoryEntry> Enumerate(CanonicalPath directory)
    {
        var dir = new DirectoryInfo(directory.Value);

        // EnumerateFileSystemInfos throws its own exceptions (DirectoryNotFoundException,
        // UnauthorizedAccessException, ...) — normalized to IOException here so
        // callers of IDirectoryEnumerator see one consistent failure shape
        // regardless of which implementation they're using.
        IEnumerator<FileSystemInfo> enumerator;
        try
        {
            enumerator = dir.EnumerateFileSystemInfos().GetEnumerator();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Unable to enumerate directory '{directory}': {ex.Message}", ex);
        }

        using (enumerator)
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = enumerator.MoveNext();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"Enumeration of '{directory}' failed partway through: {ex.Message}", ex);
                }

                if (!moved)
                {
                    yield break;
                }

                yield return ToEntry(enumerator.Current);
            }
        }
    }

    private static RawDirectoryEntry ToEntry(FileSystemInfo info) => new(
        Frn: null,
        Name: info.Name,
        IsDirectory: info.Attributes.HasFlag(FileAttributes.Directory),
        SizeBytes: info is FileInfo file ? file.Length : 0,
        CreatedUtc: info.CreationTimeUtc,
        ModifiedUtc: info.LastWriteTimeUtc,
        AccessedUtc: info.LastAccessTimeUtc,
        Attributes: info.Attributes);
}
