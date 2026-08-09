namespace Zara.Filesystem.Paths;

/// <summary>
/// Pure string-level path syntax helpers shared by <see cref="PathCanonicalizer"/>
/// and <see cref="PathValidator"/>. No disk I/O, no P/Invoke — kept separate so
/// the adversarial test suite in T05 can exercise this logic directly without
/// needing real files on disk for every case.
/// </summary>
internal static class PathSyntax
{
    /// <summary>
    /// Reserved DOS device names — legal-looking filenames that actually address
    /// a device, with or without an extension (<c>CON</c> and <c>CON.txt</c> are
    /// both reserved). Windows honors these per path *segment*, not just as a
    /// whole-path match.
    /// </summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Prefixes a path with <c>\\?\</c> (or <c>\\?\UNC\</c> for a UNC path) so it
    /// bypasses the 260-character <c>MAX_PATH</c> limit and is passed to the OS
    /// verbatim rather than being reparsed by the Win32 path-normalization layer.
    /// Idempotent: an already-prefixed path is returned unchanged.
    /// </summary>
    public static string ToExtendedLength(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path[2..];
        }

        return @"\\?\" + path;
    }

    /// <summary>
    /// True for paths that address a raw device rather than a file on a volume:
    /// the <c>\\.\</c> device namespace, or an explicit <c>\\?\GLOBALROOT</c>
    /// escape. These are never valid input to anything in Zara — see
    /// ARCHITECTURE.md §17.3 item 3.
    /// </summary>
    public static bool IsDeviceNamespace(string rawPath) =>
        rawPath.StartsWith(@"\\.\", StringComparison.Ordinal) ||
        rawPath.Contains(@"\\?\GLOBALROOT", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if any segment of the path (ignoring its extension) is a reserved
    /// DOS device name — <c>CON</c>, <c>CON.txt</c>, <c>nested\PRN\file</c>, etc.
    /// </summary>
    public static bool ContainsReservedDeviceName(string rawPath)
    {
        foreach (var rawSegment in rawPath.Split('\\', '/'))
        {
            if (rawSegment.Length == 0)
            {
                continue;
            }

            string name = rawSegment;
            int dot = name.IndexOf('.');
            if (dot >= 0)
            {
                name = name[..dot];
            }

            if (ReservedDeviceNames.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True if the path contains Alternate Data Stream syntax (<c>file.txt:hidden</c>).
    /// The leading drive-letter colon (<c>C:</c>) is not itself a stream marker
    /// and is excluded from this check.
    /// </summary>
    public static bool ContainsAlternateDataStream(string rawPath)
    {
        string path = rawPath;

        // Strip a leading "\\?\" / "\\?\UNC\" prefix and a drive letter so
        // neither is mistaken for stream syntax.
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            path = path[8..];
        }
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            path = path[4..];
        }

        if (path.Length >= 2 && path[1] == ':')
        {
            path = path[2..];
        }

        return path.Contains(':', StringComparison.Ordinal);
    }

    /// <summary>
    /// True if the path's final segment ends in a dot or a space. Windows'
    /// user-mode path normalization silently strips these, but the NTFS driver
    /// underneath does not — a classic mismatch that has been used to bypass
    /// naive filename filters. Zara refuses such paths outright rather than
    /// trying to normalize them away.
    /// </summary>
    public static bool HasTrailingDotOrSpace(string rawPath)
    {
        string trimmed = rawPath.TrimEnd('\\', '/');
        if (trimmed.Length == 0)
        {
            return false;
        }

        char last = trimmed[^1];
        return last is '.' or ' ';
    }

    /// <summary>
    /// True if <paramref name="candidate"/> is <paramref name="root"/> itself or
    /// a descendant of it, compared as whole path segments (ordinal,
    /// case-insensitive) so that e.g. <c>C:\Users\bobby</c> is correctly
    /// rejected as a descendant of <c>C:\Users\bob</c> — a plain
    /// <c>StartsWith</c> would wrongly accept it.
    /// </summary>
    public static bool IsSameOrDescendant(string candidate, string root)
    {
        string trimmedRoot = root.TrimEnd('\\');
        if (string.Equals(candidate, trimmedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = trimmedRoot + "\\";
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
