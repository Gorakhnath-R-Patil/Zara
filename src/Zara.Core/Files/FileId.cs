namespace Zara.Core.Files;

/// <summary>
/// A file's true identity: its NTFS File Reference Number scoped to the volume
/// it lives on. Unlike a path, this survives rename and move-within-volume,
/// which is what makes incremental-index rename/move detection exact instead
/// of heuristic (ARCHITECTURE.md §10.1, §11.4).
/// </summary>
/// <param name="VolumeId">The internal id of the owning row in the <c>volumes</c> table
/// (not the raw volume serial number — see <see cref="VolumeRef"/>).</param>
/// <param name="Frn">The NTFS File Reference Number (64-bit, includes the sequence number
/// in its high bytes so a reused FRN after delete is not mistaken for the old file).</param>
public readonly record struct FileId(long VolumeId, long Frn)
{
    public override string ToString() => $"{VolumeId}:{Frn:X}";
}
