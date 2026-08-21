using Zara.Core.Files;

namespace Zara.Security;

/// <summary>
/// ARCHITECTURE.md §17.2's <c>BLOCKED_ROOTS</c> set, resolved against the
/// real machine. Constructed from well-known path strings, NOT via
/// <c>IPathCanonicalizer.CanonicalizeExisting</c> — several of these
/// (<c>System Volume Information</c>, <c>$Extend</c>) require elevated
/// access even to open a handle to as a standard user, and a blocked root
/// must be blocked whether or not it happens to be currently accessible or
/// even present. Volume roots themselves (blocking e.g. <c>C:\</c> as a
/// delete target) are handled separately by <see cref="RiskClassifier"/> —
/// that's a "is this path exactly a volume root" check, not a containment
/// check, so it doesn't belong in a list of container roots.
/// </summary>
public interface IBlockedRoots
{
    IReadOnlyList<CanonicalPath> Roots { get; }
}

/// <inheritdoc cref="IBlockedRoots"/>
public sealed class BlockedRoots : IBlockedRoots
{
    public IReadOnlyList<CanonicalPath> Roots { get; }

    /// <param name="zaraDataDirectory">Overridable for tests; defaults to the
    /// real <c>%LOCALAPPDATA%\Zara</c> (§17.5) — "Zara's own data directory is
    /// in BLOCKED_ROOTS. The AI cannot be talked into deleting the audit log."</param>
    public BlockedRoots(string? zaraDataDirectory = null)
    {
        string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string systemDrive = Path.GetPathRoot(systemRoot) ?? @"C:\";
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dataDirectory = zaraDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zara");

        string[] candidates =
        [
            systemRoot,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft"),
            Path.Combine(systemDrive, "$Recycle.Bin"),
            Path.Combine(systemDrive, "System Volume Information"),
            Path.Combine(systemDrive, "$Extend"),
            Path.Combine(userProfile, "AppData", "Local", "Microsoft", "Windows"),
            dataDirectory,
        ];

        Roots = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(ToCanonicalForm)
            .ToList();
    }

    private static CanonicalPath ToCanonicalForm(string path)
    {
        string normalized = Path.GetFullPath(path).TrimEnd('\\');
        return CanonicalPath.FromCanonicalizedString(@"\\?\" + normalized);
    }
}
