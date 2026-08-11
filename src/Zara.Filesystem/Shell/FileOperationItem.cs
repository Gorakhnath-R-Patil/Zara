using Zara.Core.Files;

namespace Zara.Filesystem.Shell;

/// <summary>
/// One item within a <see cref="FileOperationBatch"/>. Which fields matter
/// depends on the batch's <see cref="FileOperationKind"/>: Copy/Move need
/// <see cref="DestinationFolder"/> (and optionally <see cref="NewName"/> to
/// rename during the operation); Rename needs only <see cref="NewName"/>;
/// Delete needs neither.
/// </summary>
public sealed record FileOperationItem(CanonicalPath Source, CanonicalPath? DestinationFolder = null, string? NewName = null);
