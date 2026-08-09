using Zara.Core.Files;

namespace Zara.Filesystem.Paths;

/// <summary>
/// The gate every raw path string passes through before it becomes a
/// <see cref="CanonicalPath"/> anywhere else in the system — see
/// ARCHITECTURE.md §17.3. Scope note: this validator handles syntactic
/// well-formedness and root containment. It deliberately does NOT hard-code
/// system directories like <c>C:\Windows</c> — that policy (BLOCKED_ROOTS,
/// risk classification) belongs to <c>Zara.Security</c>'s policy engine,
/// which decides what <paramref name="allowedRoots"/> to pass in for a given
/// operation. Keeping that decision out of this type is what lets the
/// Security layer change policy without touching path-parsing code.
/// </summary>
public interface IPathValidator
{
    /// <summary>
    /// Validates and canonicalizes <paramref name="rawPath"/>.
    /// </summary>
    /// <param name="rawPath">Untrusted input — from a user, a config file, or (never
    /// directly, per ARCHITECTURE.md §14.3) model output.</param>
    /// <param name="purpose">Governs whether the path must already exist.</param>
    /// <param name="allowedRoots">The path must be one of these roots or a descendant
    /// of one. An empty list means "no root restriction" — callers that want an
    /// unrestricted validation (e.g. read-only browsing of any fixed volume) must
    /// pass that explicitly; there is no implicit default.</param>
    ValidationResult Validate(string rawPath, PathPurpose purpose, IReadOnlyList<CanonicalPath> allowedRoots);
}
