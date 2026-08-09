namespace Zara.Filesystem.Paths;

/// <summary>
/// What the caller intends to do with a validated path. Governs whether the
/// path is required to already exist — see <see cref="PathValidator"/>.
/// </summary>
public enum PathPurpose
{
    /// <summary>The path must already exist; it will only be read.</summary>
    Read,

    /// <summary>The path must already exist; it will be enumerated as a directory.</summary>
    Enumerate,

    /// <summary>
    /// The path may or may not exist yet (e.g. a move/copy/create destination).
    /// If it doesn't exist, it is validated as a prospective path — see
    /// <see cref="IPathCanonicalizer.CanonicalizeProspective"/> for why that is
    /// advisory rather than a full security boundary.
    /// </summary>
    Write,

    /// <summary>The path must already exist; it is the source of a delete.</summary>
    Delete,
}
