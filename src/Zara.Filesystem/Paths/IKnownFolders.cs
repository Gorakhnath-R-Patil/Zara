using Zara.Core.Files;

namespace Zara.Filesystem.Paths;

/// <summary>
/// Resolves well-known Windows folders via <c>SHGetKnownFolderPath</c> rather
/// than hardcoding <c>C:\Users\...</c> anywhere — ARCHITECTURE.md §20.1.
/// Notably, <see cref="KnownFolder.Downloads"/> has no equivalent in
/// <see cref="Environment.SpecialFolder"/> (.NET's older CSIDL-based API),
/// which is the concrete reason this wrapper exists instead of just calling
/// <c>Environment.GetFolderPath</c>.
/// </summary>
public interface IKnownFolders
{
    /// <summary>Resolves a known folder. Throws <see cref="IOException"/> if it
    /// cannot be resolved (rare — a known folder is missing or redirected
    /// somewhere the OS itself considers invalid).</summary>
    CanonicalPath Get(KnownFolder folder);

    /// <summary>Non-throwing form of <see cref="Get"/>.</summary>
    bool TryGet(KnownFolder folder, out CanonicalPath path);
}
