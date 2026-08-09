using Zara.Core.Files;
using Zara.Filesystem.Enumeration;

namespace Zara.Volumes.Fallback;

/// <inheritdoc cref="IWalkScanner"/>
/// <remarks>
/// Composes an injected <see cref="IDirectoryEnumerator"/> (either
/// <c>NtDirectoryEnumerator</c> or the <c>Win32DirectoryEnumerator</c>
/// fallback — this class doesn't care which) into a recursive walk. Three
/// safety properties, all from ARCHITECTURE.md §10.2/§10.4/§11.3:
///
/// 1. <b>BFS via an explicit queue, never recursion</b> — a deep
///    <c>node_modules</c> tree is exactly how naive recursive directory
///    walkers stack-overflow.
/// 2. <b>The skip-list is applied before descending</b>, not after — Zara
///    never opens a directory it's already decided to exclude.
/// 3. <b>Reparse points are never auto-descended into</b>, full stop — the
///    entry itself is still emitted (so it's indexed as an object), but
///    walking does not follow it. Per §10.4 this is the conservative
///    baseline; resolving same-volume junctions safely is an explicit
///    "reconsider" item, not default behavior. Because of this rule, a
///    symlink cycle is structurally impossible to hit — the walk simply
///    never follows the edge that would create one. The visited-set below
///    exists anyway, as defense-in-depth against that policy ever loosening.
/// </remarks>
public sealed class WalkScanner : IWalkScanner
{
    private readonly IDirectoryEnumerator _directoryEnumerator;

    public WalkScanner(IDirectoryEnumerator directoryEnumerator)
    {
        _directoryEnumerator = directoryEnumerator ?? throw new ArgumentNullException(nameof(directoryEnumerator));
    }

    public IEnumerable<WalkedFile> Walk(CanonicalPath root, WalkOptions? options = null)
    {
        options ??= new WalkOptions();

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Value };
        var queue = new Queue<(CanonicalPath Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (directory, depth) = queue.Dequeue();

            IEnumerator<RawDirectoryEntry> entries;
            try
            {
                entries = _directoryEnumerator.Enumerate(directory).GetEnumerator();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                options.OnDirectoryError?.Invoke(directory, ex);
                continue;
            }

            using (entries)
            {
                while (true)
                {
                    RawDirectoryEntry current;
                    try
                    {
                        if (!entries.MoveNext())
                        {
                            break;
                        }
                        current = entries.Current;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Partway-through failure (e.g. the directory was
                        // deleted mid-enumeration): stop this directory, keep
                        // walking the rest of the tree.
                        options.OnDirectoryError?.Invoke(directory, ex);
                        break;
                    }

                    string childPathValue = directory.Value.TrimEnd('\\') + "\\" + current.Name;
                    var childPath = CanonicalPath.FromCanonicalizedString(childPathValue);

                    if (options.SkipList.ShouldSkip(childPathValue, current.IsDirectory))
                    {
                        continue;
                    }

                    yield return new WalkedFile(
                        childPath,
                        current.Name,
                        current.Frn,
                        current.IsDirectory,
                        current.SizeBytes,
                        current.CreatedUtc,
                        current.ModifiedUtc,
                        current.AccessedUtc,
                        current.Attributes,
                        depth + 1);

                    bool isReparsePoint = current.Attributes.HasFlag(FileAttributes.ReparsePoint);

                    if (current.IsDirectory && !isReparsePoint && depth + 1 < options.MaxDepth)
                    {
                        if (visited.Add(childPathValue))
                        {
                            queue.Enqueue((childPath, depth + 1));
                        }
                    }
                }
            }
        }
    }
}
