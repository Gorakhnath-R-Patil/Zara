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
    /// the OS for its final path — this resolves 8.3 short names and casing
    /// exactly as NTFS sees them. Throws <see cref="IOException"/> if the path
    /// cannot be opened.
    /// </summary>
    /// <remarks>
    /// Opens with <c>FILE_FLAG_OPEN_REPARSE_POINT</c>, so a symlink or
    /// junction's OWN path is returned, NOT the path it points to — this is
    /// what lets a caller like <c>WalkScanner</c> detect "this is a reparse
    /// point" (§10.4: never auto-descend into one) instead of being silently
    /// redirected to its target. For security-sensitive containment
    /// re-verification (§17.3 item 7 — "re-verify AFTER any reparse
    /// resolution"), use <see cref="ResolveFollowingReparsePoints"/> instead,
    /// which answers the opposite, equally necessary question: where does
    /// this path actually LEAD.
    /// </remarks>
    CanonicalPath CanonicalizeExisting(string path);

    /// <summary>
    /// Like <see cref="CanonicalizeExisting"/>, but follows any symlink or
    /// junction along the path to its ultimate target rather than stopping at
    /// the reparse point itself — ARCHITECTURE.md §17.3 item 7's TOCTOU
    /// defense: a path that looks safe on its own (inside an allowed root)
    /// can still be a junction whose target resolves somewhere the
    /// containment check would refuse. Security-sensitive validation must
    /// check THIS result's containment, not <see cref="CanonicalizeExisting"/>'s.
    /// </summary>
    CanonicalPath ResolveFollowingReparsePoints(string path);

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
