using System.Diagnostics;
using System.Threading.Channels;

using Devolutions.NowClient.Worker;
using Devolutions.NowProto;
using Devolutions.NowProto.Capabilities;
using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient
{
    /// <summary>
    /// NOW-proto remote execution channel client.
    /// </summary>
    public class NowClient
    {
        /// <summary>
        /// Performs connection and negotiation sequence to NOW-proto server
        /// over the provided transport (e.g. DVC-based channel) and returns
        /// a client object ready to accept commands.
        /// </summary>
        public static async Task<NowClient> Connect(INowTransport transportImpl)
        {
            var channel = new NowChannelTransport(transportImpl);

            try
            {
                return await ConnectImpl(channel);
            }
            catch
            {
                // Dispose channel if connection fails before transferring to WorkerCtx
                channel.Dispose();
                throw;
            }
        }

        private static async Task<NowClient> ConnectImpl(NowChannelTransport channel)
        {
            // Support all capabilities by default on client side.
            var clientCapabilities = new NowMsgChannelCapset.Builder()
                .HeartbeatInterval(TimeSpan.FromSeconds(60))
                .SystemCapset(NowCapabilitySystem.All)
                .SessionCapset(NowCapabilitySession.All)
                .ExecCapset(NowCapabilityExec.All)
                .Build();

            await channel.WriteMessage(clientCapabilities);

            // Wait for downgraded capabilities from server or throw timeout error.
            var capabilities = await channel.ReadMessage<NowMsgChannelCapset>()
                .WaitAsync(TimeSpan.FromSeconds(TimeoutConnectSeconds));

            // Negotiation successful
            Debug.WriteLine($"NOW channel negotiation complete");

            // client -> worker communication channel.
            // Reverse communication (e.g) worker -> client is done through passed handler objects.
            var clientChannel = Channel.CreateBounded<IClientCommand>(IoChannelCapacity);

            var ctx = new WorkerCtx
            {
                NowChannel = channel,
                Capabilities = capabilities,
                LastHeartbeat = DateTime.Now,
                Commands = clientChannel.Reader,
                HeartbeatInterval = capabilities.HeartbeatInterval,
            };

            var workerTask = Task.Run(() =>
            {
                try
                {
                    return WorkerCtx.Run(ctx);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"NowClient worker exception: {e}");
                    throw;
                }
            });

            var client = new NowClient(capabilities, workerTask, clientChannel.Writer);

            return client;
        }

        /// <summary>
        /// Send system shutdown command to the NOW-proto server.
        /// </summary>
        public async Task SystemShutdown(SystemShutdownParams shutdownParams)
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.SystemCapset.HasFlag(NowCapabilitySystem.Shutdown))
            {
                ThrowCapabilitiesError("Shutdown");
            }

            await _commandWriter.WriteAsync(new CommandSystemShutdown(shutdownParams.NowMessage));
        }

        /// <summary>
        /// Send session lock command to the NOW-proto server.
        /// </summary>
        public async Task SessionLock()
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.SessionCapset.HasFlag(NowCapabilitySession.Lock))
            {
                ThrowCapabilitiesError("Session lock");
            }

            await _commandWriter.WriteAsync(new CommandSessionLock());
        }

        /// <summary>
        /// Send session logoff command to the NOW-proto server.
        /// </summary>
        public async Task SessionLogoff()
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.SessionCapset.HasFlag(NowCapabilitySession.Logoff))
            {
                ThrowCapabilitiesError("Session logoff");
            }

            await _commandWriter.WriteAsync(new CommandSessionLogoff());
        }

        /// <summary>
        /// Send message box request to the NOW-proto server.
        /// This request will return response handler which could be
        /// used to wait for the response result.
        /// </summary>
        public async Task<MessageBoxResponse> SessionMessageBox(MessageBoxParams msgBoxParams)
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.SessionCapset.HasFlag(NowCapabilitySession.Msgbox))
            {
                ThrowCapabilitiesError("Message box");
            }

            var requestId = _nextMessageBoxId++;
            var message = msgBoxParams.ToNowMessage(requestId, true);
            var handler = new MessageBoxResponse(requestId);
            var command = new CommandSessionMsgBox(message, handler);

            await _commandWriter.WriteAsync(command);

            return handler;
        }

        /// <summary>
        /// Send message box request to the NOW-proto server.
        /// This request is fire-and-forget and does not wait for the response.
        /// </summary>
        public async Task SessionMessageBoxNoResponse(MessageBoxParams msgBoxParams)
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.SessionCapset.HasFlag(NowCapabilitySession.Msgbox))
            {
                ThrowCapabilitiesError("Message box");
            }

            var requestId = _nextMessageBoxId++;
            var message = msgBoxParams.ToNowMessage(requestId, false);
            var command = new CommandSessionMsgBox(message, null);

            await _commandWriter.WriteAsync(command);
        }

        private async Task SessionSetKbdLayoutImpl(NowMsgSessionSetKbdLayout message)
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.SessionCapset.HasFlag(NowCapabilitySession.SetKbdLayout))
            {
                ThrowCapabilitiesError("Set keyboard layout");
            }

            var command = new CommandSessionSetKbdLayout(message);
            await _commandWriter.WriteAsync(command);
        }

        /// <summary>
        /// Set the next keyboard layout for the active foreground window.
        /// </summary>
        public async Task SessionSetKbdLayoutNext()
        {
            await SessionSetKbdLayoutImpl(NowMsgSessionSetKbdLayout.Next());
        }

        /// <summary>
        /// Set the previous keyboard layout for the active foreground window.
        /// </summary>
        public async Task SessionSetKbdLayoutPrev()
        {
            await SessionSetKbdLayoutImpl(NowMsgSessionSetKbdLayout.Prev());
        }

        /// <summary>
        /// Set a specific keyboard layout for the active foreground window.
        /// </summary>
        public async Task SessionSetKbdLayoutSpecific(string layout)
        {
            await SessionSetKbdLayoutImpl(NowMsgSessionSetKbdLayout.Specific(layout));
        }

        /// <summary>
        /// Sets the callback for window recording events.
        /// </summary>
        public async Task SetWindowRecEventHandler(IWindowRecEventHandler? handler)
        {
            ThrowIfWorkerTerminated();

            await _commandWriter.WriteAsync(new CommandSetWindowRecEventHandler(handler));
        }

        /// <summary>
        /// Start window recording to track active window changes and title updates.
        /// </summary>
        /// <param name="pollInterval">Interval in milliseconds for polling window changes. Set to 0 to use the host's default.</param>
        /// <param name="trackTitleChange">Enable window title change tracking.</param>
        public async Task SessionWindowRecStart(uint pollInterval, bool trackTitleChange = false)
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.SessionCapset.HasFlag(NowCapabilitySession.WindowRecording))
            {
                ThrowCapabilitiesError("Window recording");
            }

            var message = new NowMsgSessionWindowRecStart(pollInterval, trackTitleChange);
            var command = new CommandSessionWindowRecStart(message);
            await _commandWriter.WriteAsync(command);
        }

        /// <summary>
        /// Stop window recording.
        /// </summary>
        public async Task SessionWindowRecStop()
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.SessionCapset.HasFlag(NowCapabilitySession.WindowRecording))
            {
                ThrowCapabilitiesError("Window recording");
            }

            var message = new NowMsgSessionWindowRecStop();
            var command = new CommandSessionWindowRecStop(message);
            await _commandWriter.WriteAsync(command);
        }

        /// <summary>
        /// Start a new simple remote execution session.
        /// (see <see cref="ExecRunParams"/> for more details).
        /// </summary>
        public async Task ExecRun(ExecRunParams execParams)
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.ExecCapset.HasFlag(NowCapabilityExec.Run))
            {
                ThrowCapabilitiesError("Run execution style");
            }

            var sessionId = _nextExecSessionId++;
            var message = execParams.ToNowMessage(sessionId);
            var command = new CommandExecRun(message);

            await _commandWriter.WriteAsync(command);
        }

        /// <summary>
        /// Start a new process remote execution session.
        /// See <see cref="ExecProcessParams"/> for more details.
        /// </summary>
        public async Task<ExecSession> ExecProcess(ExecProcessParams execParams)
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.ExecCapset.HasFlag(NowCapabilityExec.Process))
            {
                ThrowCapabilitiesError("Process execution style");
            }

            var sessionId = _nextExecSessionId++;
            var message = execParams.ToNowMessage(sessionId);
            var execSession = execParams.ToExecSession(sessionId, _commandWriter);
            var command = new CommandExecProcess(message, execSession);

            await _commandWriter.WriteAsync(command);

            return execSession;
        }

        /// <summary>
        /// Start a new shell remote execution session.
        /// See <see cref="ExecShellParams"/> for more details.
        /// </summary>
        public async Task<ExecSession> ExecShell(ExecShellParams execParams)
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.ExecCapset.HasFlag(NowCapabilityExec.Shell))
            {
                ThrowCapabilitiesError("Shell execution style");
            }

            var sessionId = _nextExecSessionId++;
            var message = execParams.ToNowMessage(sessionId);
            var execSession = execParams.ToExecSession(sessionId, _commandWriter);
            var command = new CommandExecShell(message, execSession);

            await _commandWriter.WriteAsync(command);

            return execSession;
        }

        /// <summary>
        /// Start a new batch remote execution session.
        /// See <see cref="ExecBatchParams"/> for more details.
        /// </summary>
        public async Task<ExecSession> ExecBatch(ExecBatchParams execParams)
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.ExecCapset.HasFlag(NowCapabilityExec.Batch))
            {
                ThrowCapabilitiesError("Batch execution style");
            }

            var sessionId = _nextExecSessionId++;
            var message = execParams.ToNowMessage(sessionId);
            var execSession = execParams.ToExecSession(sessionId, _commandWriter);
            var command = new CommandExecBatch(message, execSession);

            await _commandWriter.WriteAsync(command);

            return execSession;
        }

        /// <summary>
        /// Start a new PowerShell remote execution session.
        /// See <see cref="ExecWinPsParams"/> for more details.
        /// </summary>
        public async Task<ExecSession> ExecWinPs(ExecWinPsParams execParams)
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.ExecCapset.HasFlag(NowCapabilityExec.WinPs))
            {
                ThrowCapabilitiesError("Windows PowerShell execution style");
            }

            var sessionId = _nextExecSessionId++;
            var message = execParams.ToNowMessage(sessionId);
            var execSession = execParams.ToExecSession(sessionId, _commandWriter);
            var command = new CommandExecWinPs(message, execSession);

            await _commandWriter.WriteAsync(command);

            return execSession;
        }

        /// <summary>
        /// Start a new PowerShell Core remote execution session.
        /// See <see cref="ExecPwshParams"/> for more details.
        /// </summary>
        public async Task<ExecSession> ExecPwsh(ExecPwshParams execParams)
        {
            ThrowIfWorkerTerminated();

            if (!Capabilities.ExecCapset.HasFlag(NowCapabilityExec.Pwsh))
            {
                ThrowCapabilitiesError("Pwsh execution style");
            }

            var sessionId = _nextExecSessionId++;
            var message = execParams.ToNowMessage(sessionId);
            var execSession = execParams.ToExecSession(sessionId, _commandWriter);
            var command = new CommandExecPwsh(message, execSession);

            await _commandWriter.WriteAsync(command);

            return execSession;
        }

        /// <summary>
        /// Gracefully terminate and close the NOW-proto communication channel.
        /// </summary>
        public async Task ForceTermiate()
        {
            await _commandWriter.WriteAsync(new CommandChannelClose());
        }

        // RDM Methods

        /// <summary>
        /// Sets the callback for RDM application notifications.
        /// </summary>
        public async Task SetRdmAppNotifyHandler(RdmAppNotifyHandler? handler)
        {
            ThrowIfWorkerTerminated();

            await _commandWriter.WriteAsync(new CommandSetRdmAppNotifyHandler(handler));
        }

        /// <summary>
        /// Sets the callback for RDM session notifications.
        /// </summary>
        public async Task SetRdmSessionNotifyHandler(RdmSessionNotifyHandler? handler)
        {
            ThrowIfWorkerTerminated();

            await _commandWriter.WriteAsync(new CommandSetRdmSessionNotifyHandler(handler));
        }

        private bool _rdmCapabilitiesSent = false;
        private RdmCapabilityInfo? _rdmCapabilities = null;

        /// <summary>
        /// Performs RDM capabilities exchange with the server.
        /// This method is automatically called before any RDM app or session operations if not already called.
        /// </summary>
        public async Task RdmSync()
        {
            ThrowIfWorkerTerminated();

            var clientTimestamp = DateTimeOffset.UtcNow;
            var message = new NowMsgRdmCapabilities.Builder(
                (ulong)clientTimestamp.ToUnixTimeSeconds(),
                ""
            ).Build();

            using var responseHandler = new RdmCapabilitiesResponseHandler();
            var command = new CommandRdmCapabilities(message, responseHandler);

            await _commandWriter.WriteAsync(command);

            var response = await responseHandler.WaitForResponseAsync(TimeSpan.FromSeconds(10));

            _rdmCapabilities = new RdmCapabilityInfo(
                response.IsAppAvailable,
                response.RdmVersion,
                response.VersionExtra,
                DateTimeOffset.FromUnixTimeSeconds((long)response.Timestamp)
            );

            _rdmCapabilitiesSent = true;
        }

        /// <summary>
        /// Checks if RDM application is available on the server.
        /// Automatically performs capabilities exchange if not already done.
        /// </summary>
        public async Task<bool> IsRdmAppAvailable()
        {
            await EnsureRdmCapabilitiesSent();

            return _rdmCapabilities?.IsAppAvailable ?? false;
        }

        /// <summary>
        /// Gets the RDM version information from the server.
        /// Automatically performs capabilities exchange if not already done.
        /// </summary>
        public async Task<RdmVersion?> GetRdmVersion()
        {
            await EnsureRdmCapabilitiesSent();

            if (_rdmCapabilities == null || !_rdmCapabilities.IsAppAvailable)
                return null;

            return new RdmVersion(_rdmCapabilities.RdmVersion,
                string.IsNullOrEmpty(_rdmCapabilities.VersionExtra) ? null : _rdmCapabilities.VersionExtra);
        }

        /// <summary>
        /// Starts the RDM application on the server.
        /// Automatically performs capabilities exchange if not already done.
        /// </summary>
        public async Task RdmStart(RdmStartParams? startParams = null)
        {
            ThrowIfWorkerTerminated();
            await EnsureRdmCapabilitiesSent();

            var parameters = startParams ?? new RdmStartParams();
            var launchFlags = parameters.GetLaunchFlags();
            var timeout = parameters.GetTimeout();
            var builder = new NowMsgRdmAppStart.Builder()
                .Timeout((uint)timeout.TotalSeconds);

            if (launchFlags.HasFlag(NowRdmLaunchFlags.JumpMode))
                builder = builder.WithJumpMode();
            if (launchFlags.HasFlag(NowRdmLaunchFlags.Maximized))
                builder = builder.WithMaximized();
            if (launchFlags.HasFlag(NowRdmLaunchFlags.Fullscreen))
                builder = builder.WithFullscreen();

            var message = builder.Build();
            var command = new CommandRdmAppStart(message);

            await _commandWriter.WriteAsync(command);
        }

        /// <summary>
        /// Sends an action command to the RDM application.
        /// Automatically performs capabilities exchange if not already done.
        /// </summary>
        public async Task RdmAction(NowRdmAppAction action, string actionData = "")
        {
            ThrowIfWorkerTerminated();
            await EnsureRdmCapabilitiesSent();

            var message = new NowMsgRdmAppAction(action, actionData);
            var command = new CommandRdmAppAction(message);

            await _commandWriter.WriteAsync(command);
        }

        /// <summary>
        /// Starts a new RDM session.
        /// Automatically performs capabilities exchange if not already done.
        /// </summary>
        public async Task RdmSessionStart(RdmSessionStartParams sessionParams)
        {
            ThrowIfWorkerTerminated();
            await EnsureRdmCapabilitiesSent();

            var message = new NowMsgRdmSessionStart(
                sessionParams.SessionId,
                sessionParams.ConnectionId,
                sessionParams.ConnectionData
            );
            var command = new CommandRdmSessionStart(message);

            await _commandWriter.WriteAsync(command);
        }

        /// <summary>
        /// Sends a focus action to an RDM session.
        /// Automatically performs capabilities exchange if not already done.
        /// </summary>
        public async Task RdmSessionFocus(Guid sessionId)
        {
            ThrowIfWorkerTerminated();
            await EnsureRdmCapabilitiesSent();

            var message = new NowMsgRdmSessionAction(NowRdmSessionAction.Focus, sessionId);
            var command = new CommandRdmSessionAction(message);

            await _commandWriter.WriteAsync(command);
        }

        /// <summary>
        /// Sends a close action to an RDM session.
        /// Automatically performs capabilities exchange if not already done.
        /// </summary>
        public async Task RdmSessionClose(Guid sessionId)
        {
            ThrowIfWorkerTerminated();
            await EnsureRdmCapabilitiesSent();

            var message = new NowMsgRdmSessionAction(NowRdmSessionAction.Close, sessionId);
            var command = new CommandRdmSessionAction(message);

            await _commandWriter.WriteAsync(command);
        }

        /// <summary>
        /// Ensures that RDM capabilities have been exchanged with the server.
        /// </summary>
        private async Task EnsureRdmCapabilitiesSent()
        {
            if (!_rdmCapabilitiesSent)
            {
                // Check if server version supports RDM functionality
                if (Capabilities.Version < MIN_RDM_ENABLED_VERSION)
                {
                    throw new NowClientException(
                        $"RDM functionality requires NOW-proto server version {MIN_RDM_ENABLED_VERSION.Major}.{MIN_RDM_ENABLED_VERSION.Minor} or higher. " +
                        $"Current server version is {Capabilities.Version.Major}.{Capabilities.Version.Minor}.");
                }

                await RdmSync();
            }
        }

        private static void ThrowCapabilitiesError(string capability)
        {
            throw new NowClientException($"{capability} is not supported by server.");
        }

        private void ThrowIfWorkerTerminated()
        {
            if (_runnerTask.IsCompleted)
            {
                throw new NowClientException("NOW-proto worker has been terminated.");
            }
        }

        private NowClient(
            NowMsgChannelCapset capabilities,
            Task workerTask,
            ChannelWriter<IClientCommand> commandWriter
        )
        {
            this.Capabilities = capabilities;
            this._runnerTask = workerTask;
            this._commandWriter = commandWriter;
        }

        private const int TimeoutConnectSeconds = 10;
        private const int IoChannelCapacity = 1024;

        /// <summary>
        /// Minimum NOW-proto server version required for RDM functionality.
        /// RDM features were introduced in version 1.3, so servers with version 1.2 and below are not supported.
        /// </summary>
        private static readonly NowProtoVersion MIN_RDM_ENABLED_VERSION = new(1, 3);

        /// <summary>
        /// Check if the NOW-proto channel has been terminated.
        /// </summary>
        public bool IsTerminated => _runnerTask.IsCompleted;

        public NowMsgChannelCapset Capabilities { get; }

        private uint _nextMessageBoxId = 0;
        private uint _nextExecSessionId = 0;

        private readonly Task _runnerTask;
        private readonly ChannelWriter<IClientCommand> _commandWriter;
    }
}