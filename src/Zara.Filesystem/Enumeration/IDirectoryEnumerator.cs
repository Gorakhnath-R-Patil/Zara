using Zara.Core.Files;

namespace Zara.Filesystem.Enumeration;

/// <summary>
/// Streams the immediate children of a directory — no recursion; callers that
/// want a recursive walk compose this (see <c>WalkScanner</c>, M2+).
/// </summary>
public interface IDirectoryEnumerator
{
    /// <summary>
    /// Streams <paramref name="directory"/>'s children. Lazy: enumeration
    /// happens as the caller pulls, so a caller that stops early (e.g. found
    /// what it needed) doesn't pay for entries it never asked for.
    /// </summary>
    IEnumerable<RawDirectoryEntry> Enumerate(CanonicalPath directory);
}
