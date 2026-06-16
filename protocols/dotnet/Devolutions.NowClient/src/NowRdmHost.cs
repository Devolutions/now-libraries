namespace Devolutions.NowClient;

using System.IO.Pipes;

using Devolutions.NowProto;
using Devolutions.NowProto.Messages;

/// <summary>
/// Server-side RDM integration that listens for RDM connections via named pipe.
/// Handles capability negotiation, message exchange, and automatic reconnection.
/// Uses an event-based API for message handling.
///
/// NOTE: The pipe connection process should be assisted with external synchronization logic
/// to signal pipe readiness (e.g., Windows Named Event), OR use OS-provided mechanisms
/// to wait until the pipe is available to connect to. OnPipeReady/OnPipeDisconnected
/// events could be used to assist with the pipe readiness signaling.
/// </summary>
public class NowRdmHost : IDisposable
{
    private readonly string _pipeName;
    private readonly object _transportLock = new object();
    private NowChannelTransport? _transport;

    /// <summary>
    /// Event raised when the pipe server is created and ready for connections.
    /// Use this to signal pipe readiness to Devolutions Agent (per-session
    /// devolutions-sessions.exe process on Windows)
    ///
    /// </summary>
    public event Action? OnPipeReady;

    /// <summary>
    /// Event raised when the pipe connection is closed or lost.
    /// </summary>
    public event Action? OnPipeDisconnected;

    /// <summary>
    /// Event raised when RDM application start message is received. Devolutions Agent will
    /// send this message to RDM (which is running NowRdmHost) right after negotiation step
    /// is complete. Even if the app is already started, this message is useful on RDM side to
    /// set the initial application state which is specified in the message. (Maximized,
    /// FullScreen etc.)
    ///
    /// </summary>
    public event Action<NowMsgRdmAppStart>? OnAppStart;

    /// <summary>
    /// Event raised when RDM application action message is received. Message is fire-and-forget and
    /// does not expect any response. This message is always sent by DevolutionsAgent automatically
    /// after negotiation is complete (passthrough of original message which initiated RDM
    /// application start).
    /// </summary>
    public event Action<NowMsgRdmAppAction>? OnAppAction;

    /// <summary>
    /// Event raised when RDM session start message is received.
    /// </summary>
    public event Action<NowMsgRdmSessionStart>? OnSessionStart;

    /// <summary>
    /// Event raised when RDM session action message is received.
    /// </summary>
    public event Action<NowMsgRdmSessionAction>? OnSessionAction;

    /// <summary>
    /// Gets the named pipe name used for the Agent <-> RDM communication.
    ///
    /// For Windows, the full pipe path is `\\.\pipe\{PipeName}`
    /// </summary>
    public string PipeName => _pipeName;

    /// <summary>
    /// Creates a new RDM server for the specified pipe name.
    /// Call Run() to start listening for connections.
    /// </summary>
    /// <param name="pipeName">Named pipe name (without \\.\\pipe\ prefix) to create the pipe for.</param>
    public NowRdmHost(string pipeName)
    {
        if (string.IsNullOrEmpty(pipeName))
        {
            throw new NowRdmHostException("Pipe name cannot be null or empty");
        }

        _pipeName = pipeName;
    }

    /// <summary>
    /// Runs the RDM host server loop. This method will run indefinitely until
    /// cancellation is requested.
    /// - Creates pipe server
    /// - Waits for connection from Devolutions Agent
    /// - Negotiates capabilities
    /// - Sends READY notification
    ///
    /// Note that calling code should typically provide some reconnection logic (call `Run` again)
    /// due to fact that Agent <-> RDM connections can be lost at any time (e.g., Agent restart,
    /// session disconnect, etc.).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop the server.</param>
    public async Task Run(CancellationToken cancellationToken)
    {
        using var pipeServer = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        OnPipeReady?.Invoke();

        await pipeServer.WaitForConnectionAsync(cancellationToken);

        using var pipeTransport = new NowRdmHostPipeTransport(pipeServer);
        using var channelTransport = new NowChannelTransport(pipeTransport);

        try
        {
            await NegotiateCapabilities(channelTransport, cancellationToken);

            lock (_transportLock)
            {
                _transport = channelTransport;
            }

            // Negotiation complete - send READY notification to client
            await SendAppNotify(NowRdmAppState.Ready, NowRdmReason.NotSpecified, string.Empty);

            // Connection established successfully - process messages
            await ProcessMessages(channelTransport, cancellationToken);
        }
        finally
        {
            // Clear active transport reference
            lock (_transportLock)
            {
                _transport = null;
            }

            // Signal that pipe is disconnected
            OnPipeDisconnected?.Invoke();
        }
    }

    /// <summary>
    /// Processes incoming messages and raises appropriate events.
    /// </summary>
    private async Task ProcessMessages(NowChannelTransport transport, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await transport.ReadMessageAny(cancellationToken);

            // Dispatch message to appropriate event handler
            if (message.MessageClass == NowMessage.ClassRdm)
            {
                switch ((RdmMessageKind)message.MessageKind)
                {
                    case RdmMessageKind.AppStart:
                        var appStart = message.Deserialize<NowMsgRdmAppStart>();
                        try
                        {
                            OnAppStart?.Invoke(appStart);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Unhandled exception in OnAppStart handler: {ex}");
                        }
                        break;

                    case RdmMessageKind.AppAction:
                        var appAction = message.Deserialize<NowMsgRdmAppAction>();
                        try
                        {
                            OnAppAction?.Invoke(appAction);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Unhandled exception in OnAppAction handler: {ex}");
                        }
                        break;

                    case RdmMessageKind.SessionStart:
                        var sessionStart = message.Deserialize<NowMsgRdmSessionStart>();
                        try
                        {
                            OnSessionStart?.Invoke(sessionStart);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Unhandled exception in OnSessionStart handler: {ex}");
                        }
                        break;

                    case RdmMessageKind.SessionAction:
                        var sessionAction = message.Deserialize<NowMsgRdmSessionAction>();
                        try
                        {
                            OnSessionAction?.Invoke(sessionAction);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Unhandled exception in OnSessionAction handler: {ex}");
                        }
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Negotiates capabilities with the RDM client.
    /// </summary>
    private async Task NegotiateCapabilities(NowChannelTransport transport, CancellationToken cancellationToken)
    {
        // Read client capabilities
        var clientCapabilities = await transport.ReadMessage<NowMsgChannelCapset>(cancellationToken);

        // Validate protocol version
        if (clientCapabilities.Version.Major != 1)
        {
            throw new NowRdmHostException($"Incompatible protocol version: {clientCapabilities.Version.Major}.{clientCapabilities.Version.Minor}");
        }

        // Echo back client capabilities (no downgrading, only used as acknowledgment).
        await transport.WriteMessage(clientCapabilities);
    }

    /// <summary>
    /// Sends an application notify message to the client.
    /// </summary>
    /// <param name="appState">The application state.</param>
    /// <param name="reasonCode">The reason code for the state change.</param>
    /// <param name="notifyData">Additional notification data (optional).</param>
    /// <exception cref="NowRdmHostException">Thrown when not connected or send fails.</exception>
    public async Task SendAppNotify(NowRdmAppState appState, NowRdmReason reasonCode, string notifyData = "")
    {
        NowChannelTransport? transport;
        lock (_transportLock)
        {
            transport = _transport;
        }

        if (transport == null)
        {
            throw new NowRdmHostException("Not connected to client");
        }

        try
        {
            var message = new NowMsgRdmAppNotify(appState, reasonCode, notifyData);
            await transport.WriteMessage(message);
        }
        catch (Exception ex)
        {
            throw new NowRdmHostException("Failed to send application notify message", ex);
        }
    }

    /// <summary>
    /// Sends a session notify message to the client.
    /// </summary>
    /// <param name="sessionNotify">The session notification type.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="logData">Session log data (optional).</param>
    /// <exception cref="NowRdmHostException">Thrown when not connected or send fails.</exception>
    public async Task SendSessionNotify(NowRdmSessionNotifyKind sessionNotify, Guid sessionId, string logData = "")
    {
        NowChannelTransport? transport;
        lock (_transportLock)
        {
            transport = _transport;
        }

        if (transport == null)
        {
            throw new NowRdmHostException("Not connected to client");
        }

        try
        {
            var message = new NowMsgRdmSessionNotify(sessionNotify, sessionId, logData);
            await transport.WriteMessage(message);
        }
        catch (Exception ex)
        {
            throw new NowRdmHostException("Failed to send session notify message", ex);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the server is currently connected to a client.
    /// </summary>
    public bool IsConnected
    {
        get
        {
            lock (_transportLock)
            {
                return _transport != null;
            }
        }
    }

    /// <summary>
    /// Disposes the NowRdmHost and releases all resources.
    /// </summary>
    public void Dispose()
    {
        _transport?.Dispose();
        _transport = null;
    }
}