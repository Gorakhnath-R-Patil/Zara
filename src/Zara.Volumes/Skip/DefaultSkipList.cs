namespace Zara.Volumes.Skip;

/// <summary>
/// The hard-excluded set from ARCHITECTURE.md §10.5 — "not user-configurable".
/// User-editable exclusions (the <c>ignored_paths</c> table) are a separate,
/// later concern layered on top of this, not a replacement for it.
/// </summary>
public sealed class DefaultSkipList : ISkipList
{
    // Contiguous path-segment sequences that mean "skip this and everything
    // under it", matched case-insensitively anywhere in the path — e.g.
    // [".git", "objects"] matches "...\repo\.git\objects\..." but not
    // "...\repo\.git\config" (only the objects blob store is excluded, not
    // the whole .git directory, per §10.5's "**\.git\objects\**").
    private static readonly string[][] BlockedSegmentSequences =
    [
        ["node_modules"],
        [".git", "objects"],
        ["target", "debug"],
        ["bin", "debug"],
        [".venv"],
        ["__pycache__"],
        [".gradle"],
        ["$recycle.bin"],
        ["system volume information"],
        ["$extend"],
    ];

    private static readonly string[] BlockedFileNames =
    [
        "pagefile.sys",
        "hiberfil.sys",
        "swapfile.sys",
    ];

    private readonly string _windowsRoot;
    private readonly string _windowsFontsRoot;
    private readonly string _tempRoot;

    public DefaultSkipList()
        : this(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.GetTempPath())
    {
    }

    /// <summary>Testable constructor — takes the Windows and Temp roots explicitly
    /// instead of reading them from the live OS.</summary>
    public DefaultSkipList(string windowsRoot, string tempRoot)
    {
        _windowsRoot = windowsRoot.TrimEnd('\\');
        _windowsFontsRoot = Path.Combine(_windowsRoot, "Fonts");
        _tempRoot = tempRoot.TrimEnd('\\');
    }

    public bool ShouldSkip(string canonicalPath, bool isDirectory)
    {
        string path = StripExtendedPrefix(canonicalPath);

        if (IsUnderWindowsExceptFonts(path))
        {
            return true;
        }

        if (IsUnderRoot(path, _tempRoot))
        {
            return true;
        }

        if (!isDirectory && IsBlockedFileName(path))
        {
            return true;
        }

        return ContainsBlockedSegmentSequence(path);
    }

    private bool IsUnderWindowsExceptFonts(string path)
    {
        if (!IsUnderRoot(path, _windowsRoot))
        {
            return false;
        }

        return !IsUnderRoot(path, _windowsFontsRoot) &&
               !string.Equals(path, _windowsFontsRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderRoot(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockedFileName(string path)
    {
        string name = Path.GetFileName(path);

        foreach (string blocked in BlockedFileNames)
        {
            if (string.Equals(name, blocked, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // DumpStack.log* — prefix match, not exact.
        return name.StartsWith("DumpStack.log", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsBlockedSegmentSequence(string path)
    {
        string[] segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        foreach (var sequence in BlockedSegmentSequences)
        {
            for (int start = 0; start <= segments.Length - sequence.Length; start++)
            {
                bool matches = true;
                for (int i = 0; i < sequence.Length; i++)
                {
                    if (!string.Equals(segments[start + i], sequence[i], StringComparison.OrdinalIgnoreCase))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string StripExtendedPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }

        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }
}
