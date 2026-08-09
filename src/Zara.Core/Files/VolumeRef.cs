namespace Zara.Core.Files;

/// <summary>
/// Identifies a volume by its stable GUID rather than its drive letter, because
/// drive letters are reassignable (a USB stick moving from E: to F: must not
/// orphan its index rows) — ARCHITECTURE.md §10.1.
/// </summary>
/// <param name="Id">Internal surrogate key (the <c>volumes</c> table row id).</param>
/// <param name="VolumeGuid">The stable <c>\\?\Volume{...}</c> identity.</param>
/// <param name="SerialNumber">The NTFS volume serial number.</param>
/// <param name="DriveLetter">Current drive letter, if any — informational only, never
/// used as an identity key.</param>
/// <param name="FileSystem">e.g. "NTFS", "ReFS", "FAT32", "exFAT".</param>
/// <param name="IsRemovable">True for USB/SD media — governs opt-in indexing (§11.5).</param>
public sealed record VolumeRef(
    long Id,
    Guid VolumeGuid,
    uint SerialNumber,
    string? DriveLetter,
    string FileSystem,
    bool IsRemovable);
