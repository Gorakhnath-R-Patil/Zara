using System.Diagnostics;
using Zara.Core.Files;
using Zara.Filesystem.Enumeration;
using Zara.Filesystem.Paths;
using Zara.Volumes.Fallback;
using Zara.Volumes.Skip;

namespace Zara.Volumes.Tests.Fallback;

public class WalkScannerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly PathCanonicalizer _canonicalizer = new();
    private readonly WalkScanner _sut = new(new NtDirectoryEnumerator());

    // Every test fixture directory lives under the real OS Temp folder (like
    // any temp-based test), but DefaultSkipList's real default excludes that
    // whole tree (§10.5) — so it would skip these tests' own scratch
    // directories from the root. Tests that aren't specifically exercising
    // the skip-list use this pass-through instead; Walk_AppliesTheSkipList...
    // uses a DefaultSkipList pointed at a Windows/Temp root that doesn't
    // overlap the real filesystem, so node_modules exclusion still applies
    // without the Temp rule getting in the way.
    private static WalkOptions PassthroughOptions(int maxDepth = 64, Action<CanonicalPath, Exception>? onError = null) =>
        new() { MaxDepth = maxDepth, SkipList = new NoOpSkipList(), OnDirectoryError = onError };

    public WalkScannerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "zara-walk-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        // A junction created for Walk_DirectoryJunction_IsEmittedButNotDescendedInto
        // must be unlinked (non-recursive delete of just the reparse point)
        // before the tree delete below, or .NET's recursive delete can trip
        // over it depending on runtime version/ACL state.
        string junction = Path.Combine(_tempRoot, "junction-link");
        if (Directory.Exists(junction))
        {
            try { Directory.Delete(junction, recursive: false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Walk_DiscoversFilesAtMultipleDepths()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "root.txt"), "1");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "a"));
        File.WriteAllText(Path.Combine(_tempRoot, "a", "a1.txt"), "1");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "a", "b"));
        File.WriteAllText(Path.Combine(_tempRoot, "a", "b", "b1.txt"), "1");

        var results = _sut.Walk(Canonical(_tempRoot), PassthroughOptions()).ToList();
        var names = results.Select(r => r.Name).ToHashSet();

        Assert.Contains("root.txt", names);
        Assert.Contains("a", names);
        Assert.Contains("a1.txt", names);
        Assert.Contains("b", names);
        Assert.Contains("b1.txt", names);
    }

    [Fact]
    public void Walk_ReportsIncreasingDepthForNestedEntries()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "a", "b", "c"));
        File.WriteAllText(Path.Combine(_tempRoot, "a", "b", "c", "deep.txt"), "1");

        var results = _sut.Walk(Canonical(_tempRoot), PassthroughOptions()).ToDictionary(r => r.Name);

        Assert.Equal(1, results["a"].Depth);
        Assert.Equal(2, results["b"].Depth);
        Assert.Equal(3, results["c"].Depth);
        Assert.Equal(4, results["deep.txt"].Depth);
    }

    [Fact]
    public void Walk_EveryResultHasTheCorrectFullPath()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "sub"));
        File.WriteAllText(Path.Combine(_tempRoot, "sub", "file.txt"), "1");

        var entry = _sut.Walk(Canonical(_tempRoot), PassthroughOptions()).Single(r => r.Name == "file.txt");

        var expected = _canonicalizer.CanonicalizeExisting(Path.Combine(_tempRoot, "sub", "file.txt"));
        Assert.Equal(expected, entry.Path);
    }

    [Fact]
    public void Walk_AppliesTheSkipListBeforeDescending()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "node_modules", "lodash"));
        File.WriteAllText(Path.Combine(_tempRoot, "node_modules", "lodash", "index.js"), "1");
        File.WriteAllText(Path.Combine(_tempRoot, "real.txt"), "1");

        // Real DefaultSkipList here (that's the point of this test), but
        // pointed at a Windows/Temp root that can't overlap our real test
        // directory — see PassthroughOptions' remarks for why the parameterless
        // DefaultSkipList() can't be used directly against a temp-based fixture.
        var options = new WalkOptions
        {
            SkipList = new DefaultSkipList(windowsRoot: @"C:\Windows", tempRoot: @"C:\zara-test-unused-temp-root"),
        };
        var names = _sut.Walk(Canonical(_tempRoot), options).Select(r => r.Name).ToList();

        Assert.DoesNotContain("index.js", names);
        Assert.DoesNotContain("lodash", names);
        // The excluded directory itself is also not emitted, matching
        // ShouldSkip's isDirectory=true case for a listed name like
        // "node_modules" — see DefaultSkipListTests.
        Assert.DoesNotContain("node_modules", names);
        Assert.Contains("real.txt", names);
    }

    [Fact]
    public void Walk_RespectsMaxDepth()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "a", "b", "c"));
        File.WriteAllText(Path.Combine(_tempRoot, "a", "b", "c", "too-deep.txt"), "1");
        File.WriteAllText(Path.Combine(_tempRoot, "a", "shallow.txt"), "1");

        // MaxDepth=2: root's children are depth 1 ("a"), their children are
        // depth 2 ("b", "shallow.txt") — "b" should be emitted (it's AT the
        // cap) but never descended into, so "c" and "too-deep.txt" (depth 3+)
        // must not appear.
        var names = _sut.Walk(Canonical(_tempRoot), PassthroughOptions(maxDepth: 2)).Select(r => r.Name).ToList();

        Assert.Contains("a", names);
        Assert.Contains("b", names);
        Assert.Contains("shallow.txt", names);
        Assert.DoesNotContain("c", names);
        Assert.DoesNotContain("too-deep.txt", names);
    }

    [Fact]
    public void Walk_InaccessibleDirectory_InvokesCallbackAndContinues()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "ok-before"));
        File.WriteAllText(Path.Combine(_tempRoot, "ok-before", "a.txt"), "1");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "ok-after"));
        File.WriteAllText(Path.Combine(_tempRoot, "ok-after", "b.txt"), "1");

        // Use a scanner over a fake enumerator that throws for one specific
        // directory, to deterministically exercise the error path without
        // depending on real ACL manipulation (which needs elevation on this
        // machine to set up reliably).
        var scanner = new WalkScanner(new ThrowingEnumerator(
            new NtDirectoryEnumerator(), throwForDirectorySuffix: "ok-before"));

        var errors = new List<string>();
        var results = scanner.Walk(Canonical(_tempRoot), PassthroughOptions(
            onError: (path, _) => errors.Add(path.Value))).ToList();

        Assert.Single(errors);
        Assert.Contains("ok-before", errors[0]);
        Assert.Contains(results, r => r.Name == "ok-after");
        Assert.Contains(results, r => r.Name == "b.txt");
        Assert.DoesNotContain(results, r => r.Name == "a.txt");
    }

    [Fact]
    public void Walk_DirectoryJunction_IsEmittedButNotDescendedInto()
    {
        // The target lives OUTSIDE _tempRoot entirely — the only path by
        // which the walk could possibly reach "inside.txt" is by descending
        // through the junction. If it appeared in results, that would prove
        // real over-eager descent, not a coincidental sibling discovery.
        string target = Path.Combine(Path.GetTempPath(), "zara-walk-junction-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "inside.txt"), "1");

        string junction = Path.Combine(_tempRoot, "junction-link");

        try
        {
            if (!TryCreateJunction(junction, target))
            {
                return; // mklink unavailable in this environment — skip gracefully.
            }

            var results = _sut.Walk(Canonical(_tempRoot), PassthroughOptions()).ToList();

            Assert.Contains(results, r => r.Name == "junction-link" && r.IsReparsePoint);
            Assert.DoesNotContain(results, r => r.Name == "inside.txt");
        }
        finally
        {
            try { Directory.Delete(target, recursive: true); } catch (IOException) { }
        }
    }

    private Zara.Core.Files.CanonicalPath Canonical(string path) => _canonicalizer.CanonicalizeExisting(path);

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

    /// <summary>Never skips anything — see <see cref="PassthroughOptions"/>.</summary>
    private sealed class NoOpSkipList : ISkipList
    {
        public bool ShouldSkip(string canonicalPath, bool isDirectory) => false;
    }

    /// <summary>Wraps a real enumerator but throws for one specific directory
    /// name, so the error-handling path can be tested deterministically.</summary>
    private sealed class ThrowingEnumerator(IDirectoryEnumerator inner, string throwForDirectorySuffix) : IDirectoryEnumerator
    {
        public IEnumerable<RawDirectoryEntry> Enumerate(Zara.Core.Files.CanonicalPath directory)
        {
            if (directory.Value.EndsWith(throwForDirectorySuffix, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"Simulated denial for '{directory}'.");
            }

            return inner.Enumerate(directory);
        }
    }
}
