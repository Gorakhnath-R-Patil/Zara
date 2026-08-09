using Zara.Core.Files;
using Zara.Volumes.Skip;

namespace Zara.Volumes.Fallback;

public sealed class WalkOptions
{
    /// <summary>Hard recursion cap — ARCHITECTURE.md §10.2: "Hard depth cap: 64".
    /// Protects against pathological directory trees; a legitimate tree is
    /// nowhere near this deep.</summary>
    public int MaxDepth { get; init; } = 64;

    public ISkipList SkipList { get; init; } = new DefaultSkipList();

    /// <summary>
    /// Called when a directory can't be enumerated (access denied, I/O error).
    /// The walk continues past it rather than aborting — ARCHITECTURE.md
    /// §11.4: "record in ignored_paths... do not retry more than daily, never
    /// surface a modal." Actually recording to ignored_paths is
    /// Zara.Storage's job (a later milestone); this callback is the seam a
    /// caller uses to do that without WalkScanner itself depending on Storage.
    /// </summary>
    public Action<CanonicalPath, Exception>? OnDirectoryError { get; init; }
}
