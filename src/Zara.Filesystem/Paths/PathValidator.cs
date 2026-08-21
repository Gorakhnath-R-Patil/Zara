using Zara.Core.Files;

namespace Zara.Filesystem.Paths;

/// <inheritdoc cref="IPathValidator"/>
public sealed class PathValidator : IPathValidator
{
    // NTFS's own limit once \\?\-prefixed; a generous ceiling that exists so a
    // malicious or buggy caller can't hand us a multi-megabyte string and have
    // it flow into Win32 calls. Legitimate paths are nowhere near this.
    private const int MaxPathLength = 32_000;

    private readonly IPathCanonicalizer _canonicalizer;

    public PathValidator(IPathCanonicalizer canonicalizer)
    {
        _canonicalizer = canonicalizer ?? throw new ArgumentNullException(nameof(canonicalizer));
    }

    public ValidationResult Validate(string rawPath, PathPurpose purpose, IReadOnlyList<CanonicalPath> allowedRoots)
    {
        ArgumentNullException.ThrowIfNull(allowedRoots);

        // ── 1. Cheap syntactic rejections first — no I/O, so a flood of hostile
        //      input (e.g. from untrusted file content, per §18) can't turn into
        //      a flood of filesystem calls. ─────────────────────────────────────
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return ValidationResult.Invalid("Path is empty.");
        }

        if (rawPath.Length > MaxPathLength)
        {
            return ValidationResult.Invalid($"Path exceeds the maximum supported length of {MaxPathLength} characters.");
        }

        if (PathSyntax.IsDeviceNamespace(rawPath))
        {
            return ValidationResult.Invalid("Device namespace paths are not permitted.");
        }

        if (PathSyntax.ContainsReservedDeviceName(rawPath))
        {
            return ValidationResult.Invalid("Path contains a reserved DOS device name.");
        }

        if (PathSyntax.ContainsAlternateDataStream(rawPath))
        {
            return ValidationResult.Invalid("Alternate data stream syntax is not permitted.");
        }

        if (PathSyntax.HasTrailingDotOrSpace(rawPath))
        {
            return ValidationResult.Invalid("Path segments may not end with a trailing dot or space.");
        }

        // ── 2. Canonicalize — the only step that touches the OS. ───────────────
        bool existsOnDisk = File.Exists(rawPath) || Directory.Exists(rawPath);
        CanonicalPath canonical;
        try
        {
            canonical = ResolveCanonical(rawPath, purpose, existsOnDisk);
        }
        catch (IOException ex)
        {
            return ValidationResult.Invalid($"Unable to resolve path: {ex.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            return ValidationResult.Invalid("Access denied while resolving path.");
        }
        catch (ArgumentException ex)
        {
            // Path.GetFullPath throws this for characters Windows never allows.
            return ValidationResult.Invalid($"Path is not well-formed: {ex.Message}");
        }

        // ── 3. Root containment, on the CANONICAL form — this is what makes
        //      "C:\Users\bobby" correctly fail against an allowed root of
        //      "C:\Users\bob" (segment comparison, never a raw StartsWith)
        //      (§17.3 item 6). ─────────────────────────────────────────────────
        if (allowedRoots.Count > 0 && !IsWithinAnyRoot(canonical.Value, allowedRoots))
        {
            return ValidationResult.Invalid("Path is outside every allowed root.");
        }

        // ── 4. Re-verify AFTER reparse resolution (§17.3 item 7) — the TOCTOU
        //      defense. `canonical` above deliberately does NOT follow a
        //      symlink/junction to its target (so callers get back the
        //      reparse point's own identity — e.g. deleting it deletes the
        //      LINK, not the target). But that means step 3's containment
        //      check only proved the LINK is inside an allowed root; it says
        //      nothing about where the link actually LEADS. A junction whose
        //      own path looks perfectly safe can still point somewhere the
        //      containment check would have refused outright — so resolve it
        //      for real and check THAT too. For an ordinary (non-reparse)
        //      path this resolves to the identical location and is a no-op
        //      check. Only runs when the path already exists — a prospective
        //      write target has nothing to resolve yet, and
        //      CanonicalizeProspective's own docs already say a prospective
        //      path is advisory, not a security boundary, until it's
        //      re-validated after coming into existence. ─────────────────────
        if (allowedRoots.Count > 0 && existsOnDisk)
        {
            CanonicalPath resolvedTarget;
            try
            {
                resolvedTarget = _canonicalizer.ResolveFollowingReparsePoints(rawPath);
            }
            catch (IOException ex)
            {
                return ValidationResult.Invalid($"Unable to resolve path target: {ex.Message}");
            }
            catch (UnauthorizedAccessException)
            {
                return ValidationResult.Invalid("Access denied while resolving path target.");
            }

            if (!IsWithinAnyRoot(resolvedTarget.Value, allowedRoots))
            {
                return ValidationResult.Invalid("Path resolves (through a symlink or junction) to a location outside every allowed root.");
            }
        }

        return ValidationResult.Valid(canonical);
    }

    private CanonicalPath ResolveCanonical(string rawPath, PathPurpose purpose, bool existsOnDisk)
    {
        if (existsOnDisk)
        {
            return _canonicalizer.CanonicalizeExisting(rawPath);
        }

        if (purpose == PathPurpose.Write)
        {
            return _canonicalizer.CanonicalizeProspective(rawPath);
        }

        throw new IOException("The path does not exist.");
    }

    private static bool IsWithinAnyRoot(string canonicalPath, IReadOnlyList<CanonicalPath> allowedRoots)
    {
        foreach (var root in allowedRoots)
        {
            if (PathSyntax.IsSameOrDescendant(canonicalPath, root.Value))
            {
                return true;
            }
        }

        return false;
    }
}
