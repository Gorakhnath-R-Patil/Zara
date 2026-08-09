using Zara.Core.Files;

namespace Zara.Filesystem.Paths;

/// <summary>
/// The outcome of <see cref="IPathValidator.Validate"/>. Never throws for a bad
/// path — an invalid path is an expected, ordinary result, not an exceptional
/// one (attacker-controlled and malformed input reaches this constantly).
/// </summary>
public readonly record struct ValidationResult
{
    public bool IsValid { get; }
    public CanonicalPath Path { get; }
    public string? RejectionReason { get; }

    private ValidationResult(bool isValid, CanonicalPath path, string? rejectionReason)
    {
        IsValid = isValid;
        Path = path;
        RejectionReason = rejectionReason;
    }

    public static ValidationResult Valid(CanonicalPath path) => new(true, path, null);

    public static ValidationResult Invalid(string reason) => new(false, default, reason);
}
