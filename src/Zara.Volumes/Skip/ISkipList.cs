namespace Zara.Volumes.Skip;

/// <summary>
/// Decides whether a path should be excluded from indexing. Applied BEFORE
/// descending into a directory (ARCHITECTURE.md §11.3) — the point is to
/// never even open <c>node_modules</c>, not to open it and then discard what
/// came back.
/// </summary>
public interface ISkipList
{
    /// <param name="canonicalPath">The entry's full canonical path.</param>
    /// <param name="isDirectory">True if the entry is itself a directory (a
    /// true result on a directory means "don't descend"; on a file it means
    /// "don't index this file").</param>
    bool ShouldSkip(string canonicalPath, bool isDirectory);
}
