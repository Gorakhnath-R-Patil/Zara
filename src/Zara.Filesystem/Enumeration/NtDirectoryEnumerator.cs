using System.Buffers;
using System.Runtime.InteropServices;
using Zara.Core.Files;
using Zara.Filesystem.Interop;

namespace Zara.Filesystem.Enumeration;

/// <summary>
/// Enumerates a directory via <c>NtQueryDirectoryFile</c> (ntdll.dll,
/// undocumented but stable since Windows NT — used by Process Explorer and
/// most serious Windows file tools) requesting
/// <c>FileIdBothDirectoryInformation</c>, which returns each entry's NTFS
/// File Reference Number in the same call that returns its name and
/// metadata. That's the ~3x win over <c>FindFirstFileEx</c> described in
/// ARCHITECTURE.md §10.2: one syscall per ~400 entries instead of one
/// syscall per entry, with the FRN included for free instead of needing a
/// second per-file call to obtain it.
/// </summary>
/// <remarks>
/// NTFS-only. Throws <see cref="NotSupportedException"/> (surfaced as an
/// <see cref="IOException"/>-derived failure by the caller) on volumes that
/// don't support this information class — callers should catch that and
/// fall back to <c>Win32DirectoryEnumerator</c> (T08), which is exactly the
/// two-scanner split ARCHITECTURE.md §10.2 specifies.
/// </remarks>
public sealed class NtDirectoryEnumerator : IDirectoryEnumerator
{
    // 64 KB comfortably holds ~300-500 FILE_ID_BOTH_DIR_INFORMATION entries
    // for typical filename lengths — see ARCHITECTURE.md §10.2.
    private const int BufferSize = 64 * 1024;

    public IEnumerable<RawDirectoryEntry> Enumerate(CanonicalPath directory)
    {
        using var handle = NativeMethods.CreateFile(
            directory.Value,
            NativeMethods.FileListDirectory,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite | NativeMethods.FileShareDelete,
            lpSecurityAttributes: 0,
            NativeMethods.OpenExisting,
            NativeMethods.FileFlagBackupSemantics,
            hTemplateFile: 0);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException($"Unable to open directory '{directory}' for enumeration (Win32 error {error}).");
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            bool restart = true;
            var batch = new List<RawDirectoryEntry>(capacity: 128);

            while (true)
            {
                batch.Clear();
                int status = QueryBatch(handle, buffer, restart, batch);
                restart = false;

                foreach (var entry in batch)
                {
                    yield return entry;
                }

                if (status == NativeMethods.StatusNoMoreFiles)
                {
                    yield break;
                }

                if (status != NativeMethods.StatusSuccess)
                {
                    throw new IOException(
                        $"NtQueryDirectoryFile failed for '{directory}' with NTSTATUS 0x{status:X8}.");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Issues one NtQueryDirectoryFile call and parses every entry it
    /// returned into <paramref name="results"/>. Kept as its own method
    /// (rather than inline in the iterator) so the unsafe/fixed pointer work
    /// is fully contained within a single call frame and never has to survive
    /// across a <c>yield return</c> — an iterator method can't safely hold a
    /// pinned pointer across its state machine's suspension points.
    /// </summary>
    private static unsafe int QueryBatch(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle, byte[] buffer, bool restart, List<RawDirectoryEntry> results)
    {
        fixed (byte* bufferPtr = buffer)
        {
            int status = NativeMethods.NtQueryDirectoryFile(
                handle,
                eventHandle: 0,
                apcRoutine: 0,
                apcContext: 0,
                out _,
                fileInformation: (nint)bufferPtr,
                length: (uint)buffer.Length,
                fileInformationClass: NativeMethods.FileIdBothDirectoryInformation,
                returnSingleEntry: false,
                fileName: 0,
                restartScan: restart);

            if (status != NativeMethods.StatusSuccess)
            {
                // Covers STATUS_NO_MORE_FILES and genuine errors alike — the
                // buffer's contents are not meaningful in either case, so
                // there is nothing to parse.
                return status;
            }

            byte* current = bufferPtr;
            while (true)
            {
                var info = (NativeMethods.FileIdBothDirInformation*)current;
                int nameChars = (int)(info->FileNameLength / 2);
                var name = new string((char*)(current + sizeof(NativeMethods.FileIdBothDirInformation)), 0, nameChars);

                if (name is not "." and not "..")
                {
                    results.Add(ToEntry(*info, name));
                }

                if (info->NextEntryOffset == 0)
                {
                    break;
                }

                current += info->NextEntryOffset;
            }

            return NativeMethods.StatusSuccess;
        }
    }

    private static RawDirectoryEntry ToEntry(NativeMethods.FileIdBothDirInformation info, string name)
    {
        var attributes = (FileAttributes)info.FileAttributes;
        bool isDirectory = attributes.HasFlag(FileAttributes.Directory);

        return new RawDirectoryEntry(
            Frn: unchecked((ulong)info.FileId),
            Name: name,
            IsDirectory: isDirectory,
            SizeBytes: isDirectory ? 0 : info.EndOfFile,
            CreatedUtc: ToDateTimeOffset(info.CreationTime),
            ModifiedUtc: ToDateTimeOffset(info.LastWriteTime),
            AccessedUtc: ToDateTimeOffset(info.LastAccessTime),
            Attributes: attributes);
    }

    private static DateTimeOffset? ToDateTimeOffset(long fileTimeUtc) =>
        fileTimeUtc <= 0 ? null : DateTime.FromFileTimeUtc(fileTimeUtc);
}
