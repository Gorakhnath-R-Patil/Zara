using System.Diagnostics;
using Zara.Core.Files;
using Zara.Filesystem.Paths;
using Zara.Filesystem.Shell;

namespace Zara.Security.Tests.Suite;

/// <summary>
/// T49 / ARCHITECTURE.md §27.1's SYMLINK/TOCTOU suite. Real junctions
/// (<c>mklink /J</c>, same approach WalkScannerTests used successfully in
/// M2 — no elevation needed), real Shell delete operations, real re-
/// validation calls.
/// </summary>
/// <remarks>
/// <b>Honest scope note on the TOCTOU line specifically</b> ("swap a
/// validated path for a symlink between validate and execute -> handle-
/// based op unaffected"): ARCHITECTURE.md §17.3 item 9 names the real fix as
/// "operate on the HANDLE, not the path" — but nothing built so far
/// (<c>ShellOperations</c>/T31 included) actually threads a pre-opened
/// handle through to execution; it constructs <c>ShellItem</c>s from path
/// strings at execute time, same as most callers of <c>IFileOperation</c>
/// do. A true handle-based TOCTOU defense is therefore NOT implemented yet,
/// and this suite does not claim otherwise. What IS tested here, honestly:
/// (1) re-validation after reparse resolution is not fooled by a junction's
/// own literal path looking safe (§17.3 item 7 specifically), and (2) two
/// validations of the same literal path across a real swap produce
/// DIFFERENT, correctly-updated results — i.e. a stale validation result is
/// never silently trusted. A literal in-flight race (swap happening between
/// the OS's own internal validate-then-open inside <c>IFileOperation</c>)
/// is not something a deterministic unit test can construct, in this
/// codebase or most others; closing that gap for real is the handle-based
/// rework §17.3 item 9 describes, tracked as a real gap, not faked here.
/// </remarks>
public class SymlinkSuiteTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly PathCanonicalizer _canonicalizer = new();
    private readonly PathValidator _pathValidator;
    private readonly ShellOperations _shellOperations = new();

    public SymlinkSuiteTests()
    {
        _pathValidator = new PathValidator(_canonicalizer);
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-symlinksuite-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private CanonicalPath Canonical(string path) => _canonicalizer.CanonicalizeExisting(path);

    [Fact]
    public async Task DeletingADirectoryJunction_DeletesTheLinkOnly_TargetContentSurvives()
    {
        string targetDir = Path.Combine(Path.GetTempPath(), "zara-symlinksuite-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "precious.txt"), "do not delete me");

        string junctionPath = Path.Combine(_tempRoot, "link-to-target");
        Assert.True(TryCreateJunction(junctionPath, targetDir), "mklink /J unavailable in this environment — cannot exercise this case.");

        try
        {
            var batch = new FileOperationBatch(FileOperationKind.Delete, [new FileOperationItem(Canonical(junctionPath))]);
            var outcome = await _shellOperations.ExecuteAsync(batch);

            Assert.True(outcome.Succeeded, string.Join("; ", outcome.Items.Where(i => !i.Succeeded).Select(i => i.ErrorMessage)));
            Assert.False(Directory.Exists(junctionPath), "The junction itself should be gone.");
            Assert.True(Directory.Exists(targetDir), "The junction's TARGET directory must still exist — only the link was deleted.");
            Assert.True(File.Exists(Path.Combine(targetDir, "precious.txt")), "The target's contents must be untouched.");
        }
        finally
        {
            try { Directory.Delete(targetDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void JunctionPointingOutsideTheAllowedRoot_FailsValidation_EvenThoughItsOwnPathIsInside()
    {
        // §17.3 item 7: "Re-verify AFTER any reparse resolution." A junction
        // whose OWN path is inside the allowed root but whose TARGET
        // resolves outside it must fail — validating the literal path
        // string alone (without resolving reparse points first) would
        // wrongly pass this.
        string outsideTarget = Path.Combine(Path.GetTempPath(), "zara-symlinksuite-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideTarget);

        string junctionInsideRoot = Path.Combine(_tempRoot, "escape-hatch");
        Assert.True(TryCreateJunction(junctionInsideRoot, outsideTarget), "mklink /J unavailable in this environment — cannot exercise this case.");

        try
        {
            var allowedRoot = Canonical(_tempRoot);

            var result = _pathValidator.Validate(junctionInsideRoot, PathPurpose.Enumerate, [allowedRoot]);

            Assert.False(result.IsValid, "A junction resolving outside the allowed root must fail validation, regardless of its own literal path.");
        }
        finally
        {
            try { Directory.Delete(outsideTarget, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void RevalidatingAfterAPathIsSwappedForAJunction_ReflectsTheSwap_NeverReturnsAStaleResult()
    {
        string sensitiveElsewhere = Path.Combine(Path.GetTempPath(), "zara-symlinksuite-sensitive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sensitiveElsewhere);
        string swapPath = Path.Combine(_tempRoot, "swap-me");
        Directory.CreateDirectory(swapPath);

        var allowedRoot = Canonical(_tempRoot);

        // First validation: an ordinary real directory inside the allowed root.
        var before = _pathValidator.Validate(swapPath, PathPurpose.Enumerate, [allowedRoot]);
        Assert.True(before.IsValid);

        // The swap: remove the real directory, replace it with a junction to
        // somewhere outside the allowed root, at the EXACT SAME literal path.
        Directory.Delete(swapPath);
        Assert.True(TryCreateJunction(swapPath, sensitiveElsewhere), "mklink /J unavailable in this environment — cannot exercise this case.");

        try
        {
            // Re-validating the identical literal path string must reflect
            // the swap, not return a cached/stale "still valid" result —
            // there is no caching layer in PathValidator, and this proves it.
            var after = _pathValidator.Validate(swapPath, PathPurpose.Enumerate, [allowedRoot]);

            Assert.False(after.IsValid, "Re-validation after the swap must detect the junction now resolves outside the allowed root.");
        }
        finally
        {
            try { Directory.Delete(swapPath); } catch (IOException) { }
            try { Directory.Delete(sensitiveElsewhere, recursive: true); } catch (IOException) { }
        }
    }

    private static bool TryCreateJunction(string linkPath, string targetPath)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi)!;
            process.WaitForExit(10_000);
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }
}
