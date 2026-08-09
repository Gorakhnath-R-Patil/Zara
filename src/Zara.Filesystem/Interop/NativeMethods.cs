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
}
