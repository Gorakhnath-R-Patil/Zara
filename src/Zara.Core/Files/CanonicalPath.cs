namespace Zara.Core.Files;

/// <summary>
/// A filesystem path that has been resolved to its canonical, extended-length,
/// OS-verified form (see ARCHITECTURE.md §10.1). This type intentionally has no
/// public constructor: the only supported ways to obtain one are
/// <see cref="FromCanonicalizedString"/> — used exclusively by
/// <c>Zara.Filesystem.Paths.PathCanonicalizer</c> — or, for everyone else,
/// <c>IPathValidator.Validate(...)</c>. Nothing in the system should build a
/// <see cref="CanonicalPath"/> from a raw user or model-supplied string directly.
/// </summary>
/// <remarks>
/// Comparison and hashing are ordinal + case-insensitive, matching NTFS's own
/// uppercase table rather than any culture's casing rules — see ARCHITECTURE.md
/// §10.1 for why <c>OrdinalIgnoreCase</c> is required here and
/// <c>InvariantCultureIgnoreCase</c> is not.
/// </remarks>
public readonly struct CanonicalPath : IEquatable<CanonicalPath>
{
    private readonly string? _value;

    private CanonicalPath(string value) => _value = value;

    /// <summary>
    /// Wraps a string that the caller has ALREADY canonicalized via the OS
    /// (extended-length prefixed, symlinks/short-names/case resolved). This is
    /// a trust boundary, not a validation step — callers outside
    /// <c>PathCanonicalizer</c> should not use this.
    /// </summary>
    public static CanonicalPath FromCanonicalizedString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new CanonicalPath(value);
    }

    /// <summary>The canonical path string, e.g. <c>\\?\C:\Users\bob\file.txt</c>.</summary>
    public string Value => _value ?? throw new InvalidOperationException(
        "This CanonicalPath is default(CanonicalPath) and was never assigned a value.");

    /// <summary>True for the uninitialized <c>default(CanonicalPath)</c> value.</summary>
    public bool IsDefault => _value is null;

    public override string ToString() => _value ?? string.Empty;

    public bool Equals(CanonicalPath other) =>
        string.Equals(_value, other._value, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is CanonicalPath other && Equals(other);

    public override int GetHashCode() =>
        _value is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(_value);

    public static bool operator ==(CanonicalPath left, CanonicalPath right) => left.Equals(right);
    public static bool operator !=(CanonicalPath left, CanonicalPath right) => !left.Equals(right);
}
