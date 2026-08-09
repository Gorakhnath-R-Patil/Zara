using System.Runtime.InteropServices;
using System.Text;
using Zara.Core.Files;
using Zara.Filesystem.Interop;

namespace Zara.Filesystem.Paths;

/// <inheritdoc cref="IPathCanonicalizer"/>
public sealed class PathCanonicalizer : IPathCanonicalizer
{
    // Initial guess for GetFinalPathNameByHandle's output buffer. Almost every
    // real path fits comfortably; GetFinalPath grows this and retries on the
    // rare path that doesn't, so this is a perf tuning knob, not a correctness
    // one — see the loop in CanonicalizeExisting.
    private const int InitialBufferChars = 512;

    public CanonicalPath CanonicalizeExisting(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // A \\?\-prefixed path is passed to the OS VERBATIM — that's the whole
        // point of the prefix, it's what lets it exceed MAX_PATH — which means
        // Windows will NOT collapse "." / ".." segments in it the way it does
        // for an ordinary path. So any relative segments must be resolved
        // lexically (Path.GetFullPath, no disk I/O) BEFORE the prefix goes on;
        // prefixing first and letting CreateFile see a literal "\a\b\..\..\c"
        // fails whenever the intermediate "a\b" doesn't itself exist on disk,
        // even though the fully-resolved target does.
        string normalized = Path.GetFullPath(path);
        string extended = PathSyntax.ToExtendedLength(normalized);

        using var handle = NativeMethods.CreateFile(
            extended,
            dwDesiredAccess: 0, // metadata only
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite | NativeMethods.FileShareDelete,
            lpSecurityAttributes: 0,
            NativeMethods.OpenExisting,
            NativeMethods.FileFlagBackupSemantics | NativeMethods.FileFlagOpenReparsePoint,
            hTemplateFile: 0);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException($"Unable to open '{path}' to canonicalize it (Win32 error {error}).");
        }

        var buffer = new StringBuilder(InitialBufferChars);
        uint required = NativeMethods.GetFinalPathNameByHandle(
            handle, buffer, (uint)buffer.Capacity, NativeMethods.FileNameNormalized | NativeMethods.VolumeNameDos);

        if (required == 0)
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException($"GetFinalPathNameByHandle failed for '{path}' (Win32 error {error}).");
        }

        if (required > buffer.Capacity)
        {
            // The path was longer than our initial guess; the returned value is
            // the required buffer size (including the null terminator), so retry
            // once with exactly that much room.
            buffer.Capacity = (int)required;
            required = NativeMethods.GetFinalPathNameByHandle(
                handle, buffer, required, NativeMethods.FileNameNormalized | NativeMethods.VolumeNameDos);

            if (required == 0)
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException($"GetFinalPathNameByHandle failed for '{path}' (Win32 error {error}).");
            }
        }

        string resolved = buffer.ToString(0, (int)required);
        return CanonicalPath.FromCanonicalizedString(resolved);
    }

    public CanonicalPath CanonicalizeProspective(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Path.GetFullPath resolves "." / ".." segments and relative roots
        // lexically (no disk I/O), and throws ArgumentException on characters
        // Windows never allows in a path — exactly the "string-only
        // normalization" this method promises.
        string full = Path.GetFullPath(path);
        return CanonicalPath.FromCanonicalizedString(PathSyntax.ToExtendedLength(full));
    }
}
