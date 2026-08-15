using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Grpc.Core;
using GrpcDotNetNamedPipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Zara.Engine.Hosting;

/// <summary>
/// Hosts the gRPC-over-named-pipes server as an <see cref="IHostedService"/>
/// inside the .NET Generic Host — ARCHITECTURE.md §31's "Zara.Engine ...
/// .NET Generic Host" and §17.4's security requirement: "the gRPC transport
/// is a named pipe with a DACL restricted to the current user SID".
/// </summary>
public sealed class GrpcNamedPipeHostedService : IHostedService, IDisposable
{
    private readonly string _pipeName;
    private readonly Action<ServiceBinderBase> _bindServices;
    private readonly ILogger<GrpcNamedPipeHostedService> _logger;
    private NamedPipeServer? _server;

    public GrpcNamedPipeHostedService(
        string pipeName, Action<ServiceBinderBase> bindServices, ILogger<GrpcNamedPipeHostedService> logger)
    {
        _pipeName = pipeName;
        _bindServices = bindServices;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = new NamedPipeServerOptions
        {
            PipeSecurity = BuildOwnerOnlyPipeSecurity(),
        };

        _server = new NamedPipeServer(_pipeName, options);
        _bindServices(_server.ServiceBinder);
        _server.Start();

        _logger.LogInformation("gRPC named pipe server listening on \\\\.\\pipe\\{PipeName}", _pipeName);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _server?.Kill();
        return Task.CompletedTask;
    }

    public void Dispose() => _server?.Dispose();

    /// <summary>
    /// Restricts the pipe to the current user's SID, with no other rules —
    /// ARCHITECTURE.md §17.4's intent ("a DACL restricted to the current
    /// user SID"), achieved correctly rather than as originally written here.
    /// </summary>
    /// <remarks>
    /// <b>Found by actually running this against a real client, not by
    /// inspection:</b> the first version of this method added a single Allow
    /// rule for the current user's SID PLUS an explicit Deny rule for
    /// <c>Everyone</c> "for defense-in-depth". That's wrong: the current
    /// user is themselves a member of <c>Everyone</c>, and Windows
    /// canonicalizes ACLs with Deny ACEs evaluated before Allow ACEs
    /// regardless of insertion order — so the Deny-Everyone rule shadowed
    /// the Allow rule for the owner too, and every client (including the
    /// legitimate one) got <c>UnauthorizedAccessException</c> connecting.
    /// A `PipeSecurity` with no rule for a principal already denies that
    /// principal by default (Windows access control is deny-by-default) —
    /// the single Allow rule below is sufficient on its own; adding a
    /// "belt and suspenders" Deny rule for a group the allowed principal
    /// also belongs to is actively harmful, not merely redundant.
    /// </remarks>
    private static PipeSecurity BuildOwnerOnlyPipeSecurity()
    {
        var security = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Unable to resolve the current user's SID.");

        security.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return security;
    }
}
