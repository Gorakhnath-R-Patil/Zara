using NetArchTest.Rules;

namespace Zara.ArchitectureTests;

/// <summary>
/// Enforces the module-boundary table in ARCHITECTURE.md §8.2 in code, so a
/// layering violation fails the build instead of surviving as an unenforced
/// convention. Add a rule here for every new project as it's created —
/// this file currently only covers the two projects that exist as of M1
/// (T01–T10); the commented-out rules below are the ones §8.2 already
/// specifies for modules that don't exist yet.
/// </summary>
public class ModuleBoundaryTests
{
    [Fact]
    public void Core_HasNoDependencyOnAnyOtherZaraAssembly()
    {
        // Zara.Core is domain types + interfaces only. Everything else in the
        // system depends on it; it must depend on nothing of ours.
        var result = Types.InAssembly(typeof(Zara.Core.Files.CanonicalPath).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Zara.Filesystem", "Zara.Storage", "Zara.Indexing",
                                    "Zara.Search", "Zara.Ai", "Zara.Agent", "Zara.Security",
                                    "Zara.Engine", "Zara.App", "Zara.Volumes")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Filesystem_DoesNotDependOnStorageAiOrUi()
    {
        // Zara.Filesystem owns Win32 enumeration, shell, and paths. It must not
        // know SQLite exists, must not know an LLM exists, and must not know
        // anything about WPF/the UI — per the §8.2 table.
        var result = Types.InAssembly(typeof(Zara.Filesystem.Paths.PathValidator).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Zara.Storage", "Zara.Ai", "Zara.Agent", "Zara.App",
                                    "Microsoft.Data.Sqlite")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    // ── Not yet enforceable — the target assemblies don't exist yet.
    //    Un-comment and wire up as each milestone lands (tracker references
    //    the task in brackets):
    //
    //  [Fact] // M2 (T11+)
    //  void Storage_DoesNotDependOnAiOrUi() => ...
    //
    //  [Fact] // M8 (T41+)
    //  void Ai_DoesNotDependOnFilesystemDirectly() =>
    //      // Zara.Ai must reach the filesystem only through Zara.Search's
    //      // interfaces, never by calling Zara.Filesystem directly — §8.2.
    //      ...
    //
    //  [Fact] // M7 (T36+)
    //  void Nothing_ExceptApp_DependsOnApp() => ...

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Violating types: " + string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? []);
}
