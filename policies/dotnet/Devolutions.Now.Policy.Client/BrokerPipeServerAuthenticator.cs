using System.IO.Pipes;

using Microsoft.Win32.SafeHandles;

namespace Devolutions.Now.Policy.Client;

/// <summary>
/// Authenticates the server of a connected package broker pipe before any request data is sent.
/// </summary>
/// <param name="context">The connected pipe and the server process information gathered by the transport.</param>
/// <param name="cancellationToken">Canceled when the caller gives up or the authentication timeout elapses.</param>
/// <returns>
/// An optional value kept alive, and disposed, once the exchange completes, for example handles that pin the
/// verified server state. If the transport stops waiting, the value is disposed when the authentication completes.
/// </returns>
/// <remarks>Throw to reject the server. A thrown <see cref="BrokerClientException"/> is propagated as is.</remarks>
public delegate ValueTask<IDisposable?> BrokerPipeServerAuthenticator(
    BrokerPipeServerContext context,
    CancellationToken cancellationToken);

/// <summary>Connected package broker pipe passed to a <see cref="BrokerPipeServerAuthenticator"/>.</summary>
public sealed class BrokerPipeServerContext
{
    internal BrokerPipeServerContext(
        PipeStream pipe,
        string pipeName,
        string endpoint,
        int? serverProcessId,
        SafeProcessHandle? serverProcess)
    {
        Pipe = pipe;
        PipeName = pipeName;
        Endpoint = endpoint;
        ServerProcessId = serverProcessId;
        ServerProcess = serverProcess;
    }

    /// <summary>The connected pipe. No request data has been written yet. Owned by the transport.</summary>
    public PipeStream Pipe { get; }

    /// <summary>Name of the connected pipe.</summary>
    public string PipeName { get; }

    /// <summary>Broker endpoint the pending request targets.</summary>
    public string Endpoint { get; }

    /// <summary>Process id of the pipe server reported by the kernel, when available (Windows only).</summary>
    public int? ServerProcessId { get; }

    /// <summary>
    /// Handle to the pipe server process opened with limited query access, when the caller is allowed to open it
    /// (Windows only; typically elevated callers). The transport holds it until the exchange completes, so the
    /// process id cannot be reused meanwhile. Owned by the transport; do not dispose it.
    /// </summary>
    public SafeProcessHandle? ServerProcess { get; }
}