using Microsoft.Extensions.Hosting;
using Zara.Engine;
using Zara.Engine.Hosting;

// --pipe-name=... and --db-path=... let tests (and EngineProcessManager,
// when it needs to) spawn an isolated instance rather than colliding with
// a real running Engine or another test's instance. Falls back to the real
// per-user defaults when not supplied.
string pipeName = GetArgValue(args, "--pipe-name") ?? PipeNameProvider.GetEnginePipeName();
string dbPath = GetArgValue(args, "--db-path") ?? EngineHost.DefaultDbPath();
string? scanRoot = GetArgValue(args, "--scan-root");

using var host = EngineHost.Build(args, dbPath, pipeName, scanRoot);
await host.RunAsync();

static string? GetArgValue(string[] args, string name)
{
    string prefix = name + "=";
    foreach (string arg in args)
    {
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return arg[prefix.Length..];
        }
    }

    return null;
}
