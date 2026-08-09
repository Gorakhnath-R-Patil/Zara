using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Zara.Filesystem.Interop;

/// <summary>
/// Minimal, hand-written kernel32 P/Invoke surface. Deliberately small and
/// reviewed rather than generated: this is the layer everything in
/// ARCHITECTURE.md §17.3 (path validation) sits on top of, so its correctness
/// is a security property, not just a functionality one.
/// </summary>
internal static class NativeMethods
{
    // dwDesiredAccess: 0 means "query metadata only" — no read/write access is
    // requested, which is all CanonicalizeExisting needs and keeps this call
    // from participating in sharing-violation contention with whatever else
    // has the file open.
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint FileShareDelete = 0x00000004;
    internal const uint OpenExisting = 3;

    // Required to open a handle to a directory, and to open a reparse point
    // itself rather than transparently following it — the latter matters
    // because CanonicalizeExisting must report what the reparse point IS,
    // not silently resolve through it (that resolution is a deliberate,
    // separate step — see PathCanonicalizer.CanonicalizeExisting remarks).
    internal const uint FileFlagBackupSemantics = 0x02000000;
    internal const uint FileFlagOpenReparsePoint = 0x00200000;

    internal const uint FileNameNormalized = 0x0;
    internal const uint VolumeNameDos = 0x0;

    internal const int InvalidFileAttributes = -1;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    // ── Known folders (SHGetKnownFolderPath) ────────────────────────────────
    // KF_FLAG_DEFAULT: no special handling — don't create the folder if it's
    // missing, don't force a sync of an unhydrated cloud placeholder, etc.
    internal const uint KfFlagDefault = 0x00000000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        nint hToken,
        out nint ppszPath);

    [DllImport("ole32.dll")]
    internal static extern void CoTaskMemFree(nint pv);

    // ── Directory enumeration (NtQueryDirectoryFile) ────────────────────────
    // FILE_LIST_DIRECTORY and FILE_READ_DATA are numerically the same access
    // right (0x0001); this name is used when opening a directory specifically
    // to enumerate its contents.
    internal const uint FileListDirectory = 0x00000001;

    // FILE_INFORMATION_CLASS.FileIdBothDirectoryInformation — returns each
    // entry's NTFS File Reference Number in the same call as its name and
    // metadata, which is the whole reason this API is used over
    // FindFirstFileEx (ARCHITECTURE.md §10.2).
    internal const int FileIdBothDirectoryInformation = 37;

    internal const int StatusSuccess = 0;
    internal const int StatusNoMoreFiles = unchecked((int)0x80000006);

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoStatusBlock
    {
        // This is a NTSTATUS/PVOID union in the real struct; pointer-sized so
        // the overall layout matches on our win-x64-only target.
        public nint Status;
        public nuint Information;
    }

    /// <summary>
    /// Mirrors FILE_ID_BOTH_DIR_INFORMATION field-for-field so the CLR's
    /// default Sequential layout computes the same padding/offsets the
    /// Windows headers do — deliberately NOT Pack-overridden. FileName is a
    /// variable-length trailing array in the real struct and is read
    /// separately via pointer arithmetic past the end of this fixed part
    /// (see NtDirectoryEnumerator).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct FileIdBothDirInformation
    {
        public uint NextEntryOffset;
        public uint FileIndex;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public long EndOfFile;
        public long AllocationSize;
        public uint FileAttributes;
        public uint FileNameLength;
        public uint EaSize;
        public sbyte ShortNameLength;
        public fixed char ShortName[12];
        public long FileId;
    }

    [DllImport("ntdll.dll", SetLastError = true)]
    internal static extern int NtQueryDirectoryFile(
        SafeFileHandle fileHandle,
        nint eventHandle,
        nint apcRoutine,
        nint apcContext,
        out IoStatusBlock ioStatusBlock,
        nint fileInformation,
        uint length,
        int fileInformationClass,
        [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry,
        nint fileName,
        [MarshalAs(UnmanagedType.U1)] bool restartScan);
}
