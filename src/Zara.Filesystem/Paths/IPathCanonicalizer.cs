using Zara.Core.Files;

namespace Zara.Filesystem.Paths;

/// <summary>
/// Resolves a possibly-relative, possibly-short-named, possibly-symlinked path
/// to its canonical, OS-verified form. This is the only supported source of a
/// <see cref="CanonicalPath"/> outside of tests — see ARCHITECTURE.md §10.1.
/// </summary>
public interface IPathCanonicalizer
{
    /// <summary>
    /// Canonicalizes a path that must already exist, by opening it and asking
    /// the OS for its final path — this resolves 8.3 short names, symlinks,
    /// mount points, and casing exactly as NTFS sees them. Throws
    /// <see cref="IOException"/> if the path cannot be opened.
    /// </summary>
    CanonicalPath CanonicalizeExisting(string path);

    /// <summary>
    /// Canonicalizes a path that may not exist yet (e.g. a move/copy/create
    /// destination) using string-only normalization — no OS round-trip, no
    /// symlink resolution, because there is nothing on disk yet to resolve.
    /// Callers MUST re-canonicalize via <see cref="CanonicalizeExisting"/>
    /// after the path comes into existence; a prospective path is advisory,
    /// not a security boundary (see ARCHITECTURE.md §17.3 item 7, the
    /// TOCTOU note).
    /// </summary>
    CanonicalPath CanonicalizeProspective(string path);
}
