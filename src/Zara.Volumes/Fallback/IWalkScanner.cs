using Zara.Core.Files;

namespace Zara.Volumes.Fallback;

/// <summary>Recursively walks a directory tree, composing an <c>IDirectoryEnumerator</c>
/// for the per-directory listing — see ARCHITECTURE.md §11.3.</summary>
public interface IWalkScanner
{
    IEnumerable<WalkedFile> Walk(CanonicalPath root, WalkOptions? options = null);
}
