using System.Runtime.InteropServices;
using Zara.Core.Files;
using Zara.Filesystem.Interop;

namespace Zara.Filesystem.Paths;

/// <inheritdoc cref="IKnownFolders"/>
public sealed class KnownFolders : IKnownFolders
{
    // Well-known FOLDERID_* GUIDs from the Windows SDK's KnownFolders.h —
    // these are stable, documented OS constants, not something Zara assigns.
    private static readonly IReadOnlyDictionary<KnownFolder, Guid> FolderIds = new Dictionary<KnownFolder, Guid>
    {
        [KnownFolder.Profile] = new("5E6C858F-0E22-4760-9AFE-EA3317B67173"),
        [KnownFolder.Desktop] = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
        [KnownFolder.Documents] = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
        [KnownFolder.Downloads] = new("374DE290-123F-4565-9164-39C4925E467B"),
        [KnownFolder.Pictures] = new("33E28130-4E1E-4676-835A-98395C3BC3BB"),
        [KnownFolder.Videos] = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC"),
        [KnownFolder.Music] = new("4BD8D571-6D19-48D3-BE97-422220080E43"),
        [KnownFolder.LocalAppData] = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091"),
        [KnownFolder.RoamingAppData] = new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D"),
    };

    private readonly IPathCanonicalizer _canonicalizer;

    public KnownFolders(IPathCanonicalizer canonicalizer)
    {
        _canonicalizer = canonicalizer ?? throw new ArgumentNullException(nameof(canonicalizer));
    }

    public CanonicalPath Get(KnownFolder folder)
    {
        if (!TryGet(folder, out var path))
        {
            throw new IOException($"Unable to resolve known folder '{folder}'.");
        }

        return path;
    }

    public bool TryGet(KnownFolder folder, out CanonicalPath path)
    {
        Guid id = FolderIds[folder];
        int hr = NativeMethods.SHGetKnownFolderPath(id, NativeMethods.KfFlagDefault, hToken: 0, out nint rawPtr);

        if (hr != 0 || rawPtr == 0)
        {
            path = default;
            return false;
        }

        try
        {
            string? raw = Marshal.PtrToStringUni(rawPtr);
            if (string.IsNullOrEmpty(raw))
            {
                path = default;
                return false;
            }

            // Round-trip through the real canonicalizer rather than trusting
            // the shell's string verbatim: it resolves case/short-name/
            // reparse-point differences the same way every other path in the
            // system does, so a known folder is never a special case for
            // anything downstream (e.g. PathValidator's root-containment check).
            path = _canonicalizer.CanonicalizeExisting(raw);
            return true;
        }
        catch (IOException)
        {
            path = default;
            return false;
        }
        finally
        {
            NativeMethods.CoTaskMemFree(rawPtr);
        }
    }
}
