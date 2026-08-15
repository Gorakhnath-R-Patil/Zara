using System.Diagnostics;
using System.Reflection;
using Grpc.Core;
using Zara.Contracts.Admin;

namespace Zara.Engine.Services;

public sealed class AdminServiceImpl : AdminService.AdminServiceBase
{
    // Stopwatch, not DateTime.UtcNow subtraction — found by running this
    // test in a sandboxed/virtualized environment, where two DateTime.UtcNow
    // calls milliseconds apart occasionally disagreed enough to produce a
    // negative "elapsed" value. DateTime.UtcNow is not guaranteed
    // monotonic (it can step backward across a clock sync adjustment);
    // Stopwatch is built specifically to be immune to that for elapsed-time
    // measurements, which is exactly what uptime is.
    private static readonly Stopwatch Uptime = Stopwatch.StartNew();

    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context) =>
        Task.FromResult(new PingResponse
        {
            ProcessId = Environment.ProcessId,
            UptimeSeconds = Uptime.Elapsed.TotalSeconds,
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
        });
}
