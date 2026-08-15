using System.Security.Principal;

namespace Zara.Engine.Hosting;

/// <summary>
/// The named pipe the Engine listens on — <c>zara-engine-{sid}</c>, scoped
/// per-user (ARCHITECTURE.md §8.1's diagram) so two users on the same
/// machine, or two isolated sessions, never collide on one pipe name.
/// </summary>
public static class PipeNameProvider
{
    public static string GetEnginePipeName()
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
        return $"zara-engine-{sid}";
    }
}
