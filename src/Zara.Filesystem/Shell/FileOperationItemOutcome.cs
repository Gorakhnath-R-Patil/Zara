using Zara.Core.Files;

namespace Zara.Filesystem.Shell;

/// <param name="Source">The original item, as requested.</param>
/// <param name="Succeeded">Whether this specific item succeeded — one item
/// failing (e.g. a sharing violation) does not stop the rest of the batch.</param>
/// <param name="ResultPath">Where the item ended up (new path for a
/// move/rename/copy) — null on failure, or for a delete (nothing to point
/// to; §19.1 notes Recycle Bin restoration needs a stored recycle-bin item
/// identifier, which is a separate follow-up, not this field).</param>
/// <param name="ErrorMessage">Present only when <see cref="Succeeded"/> is false.</param>
public sealed record FileOperationItemOutcome(CanonicalPath Source, bool Succeeded, CanonicalPath? ResultPath, string? ErrorMessage);
