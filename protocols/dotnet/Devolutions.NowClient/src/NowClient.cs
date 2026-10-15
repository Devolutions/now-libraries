using System.Diagnostics;
using System.Threading.Channels;

using Devolutions.NowClient.Execution;
using Devolutions.NowClient.Worker;
using Devolutions.NowProto;
using Devolutions.NowProto.Capabilities;
using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient
{
    /// <summary>
    /// NOW-proto remote execution channel client.
    /// </summary>
    public class NowClient : IAsyncDisposable
    {
        /// <summary>
        /// Performs connection and negotiation sequence to NOW-proto server
        /// over the provided transport (e.g. DVC-based channel) and returns
        /// a client object ready to accept commands.
        /// </summary>
        public static Task<NowClient> Connect(INowTransport transportImpl)
        {
            return Connect(transportImpl, new NowClientOptions());
        }

        /// <summary>
        /// Connects using the selected execution mode. The client owns the transport, including
        /// on negotiation failure. Cancellation applies to the entire connection lifetime.
        /// </summary>
        public static Task<NowClient> Connect(
            INowTransport transportImpl,
            NowClientOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(transportImpl);
            ArgumentNullException.ThrowIfNull(options);
            if (options.UseDedicatedThread && options.Execution != null)
            {
                throw new ArgumentException("Choose either an owned thread or a supplied execution context.", nameof(options));
            }
            if (options.ConnectTimeout <= TimeSpan.Zero || options.ConnectTimeout.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "ConnectTimeout must be a positive finite timeout.");
            }

            if (options.Execution != null)
                return ConnectBorrowed(transportImpl, options.ConnectTimeout, options.Execution, cancellationToken);

            return options.UseDedicatedThread
                ? ConnectDedicated(transportImpl, options.ConnectTimeout, cancellationToken)
                : ConnectDefault(transportImpl, options.ConnectTimeout, cancellationToken);
        }

        private static async Task<NowClient> ConnectDefault(
            INowTransport transport, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var lifetime = new ClientLifetime(null, cancellationToken);
            var channel = new NowChannelTransport(transport, null, lifetime.Token);
            try
            {
                var capabilities = await Negotiate(channel, timeout, null, lifetime.Token);
                var ctx = CreateWorker(channel, capabilities, null, lifetime.Token);
                var workerTask = Task.Run(async () =>
                {
                    try
                    {
                        await RunWorker(ctx);
                    }
                    finally
                    {
                        lifetime.Dispose();
                    }
                });
                return new NowClient(capabilities, workerTask, ctx.CommandWriter, null, lifetime);
            }
            catch
            {
                try
                {
                    channel.Dispose();
                }
                finally
                {
                    lifetime.Dispose();
                }
                throw;
            }
        }

        private static Task<NowClient> ConnectDedicated(
            INowTransport transport, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var execution = new DedicatedExecution();
            var lifetime = new ClientLifetime(execution, cancellationToken);
            var channel = new NowChannelTransport(transport, execution, lifetime.Token);
            var ready = new TaskCompletionSource<NowClient>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<Task> run = async () =>
            {
                try
                {
                    try
                    {
                        var capabilities = await Negotiate(channel, timeout, execution, lifetime.Token);
                        var ctx = CreateWorker(channel, capabilities, execution, lifetime.Token);
                        var client = new NowClient(capabilities, execution.Completion, ctx.CommandWriter, execution, lifetime);
                        ready.SetResult(client);
                        await RunWorker(ctx);
                    }
                    finally
                    {
                        try
                        {
                            channel.Dispose();
                        }
                        finally
                        {
                            lifetime.Dispose();
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !ready.Task.IsCompleted)
                {
                    ready.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    if (!ready.TrySetException(exception))
                    {
                        throw;
                    }
                }
            };
            try
            {
                execution.Start(run);
            }
            catch
            {
                try
                {
                    channel.Dispose();
                }
                finally
                {
                    lifetime.Dispose();
                }
                throw;
            }
            return ready.Task;
        }

        private static Task<NowClient> ConnectBorrowed(
            INowTransport transport, TimeSpan timeout, NowClientExecution owner, CancellationToken cancellationToken)
        {
            var ready = new TaskCompletionSource<NowClient>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var lifetime = new ClientLifetime(owner.Context, cancellationToken);
            var channel = new NowChannelTransport(transport, owner.Context, lifetime.Token);
            owner.Attach(lifetime);
            try
            {
                _ = owner.Run(async () =>
                {
                    try
                    {
                        try
                        {
                            var capabilities = await Negotiate(channel, timeout, owner.Context, lifetime.Token);
                            var ctx = CreateWorker(channel, capabilities, owner.Context, lifetime.Token);
                            ready.SetResult(new NowClient(capabilities, completion.Task, ctx.CommandWriter, owner.Context, lifetime));
                            await RunWorker(ctx);
                        }
                        finally
                        {
                            try
                            {
                                channel.Dispose();
                            }
                            finally
                            {
                                lifetime.Dispose();
                                owner.Detach(lifetime);
                            }
                        }
                        completion.TrySetResult();
                    }
                    // StopAsync or execution disposal cancels the lifetime without the caller's token. The filter runs
                    // before the inner finally, so it still sees the token as it was when the exception was raised.
                    catch (OperationCanceledException) when (lifetime.Token.IsCancellationRequested && !ready.Task.IsCompleted)
                    {
                        ready.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : lifetime.Token);
                        completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        if (ready.TrySetException(exception))
                            completion.TrySetResult();
                        else
                            completion.TrySetException(exception);
                    }
                });
            }
            catch
            {
                try
                {
                    channel.Dispose();
                }
                finally
                {
                    lifetime.Dispose();
                    owner.Detach(lifetime);
                }
                throw;
            }
            return ready.Task;
        }

        private static Task<NowMsgChannelCapset> Negotiate(
            NowChannelTransport channel, TimeSpan timeout, DedicatedExecution? execution, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ProtocolTasks.WaitAsync(NegotiateImpl(channel), timeout, execution, cancellationToken);
        }

        private static async Task<NowMsgChannelCapset> NegotiateImpl(NowChannelTransport channel)
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
            var capabilities = await channel.ReadMessage<NowMsgChannelCapset>();
            if (capabilities.HeartbeatInterval == TimeSpan.Zero)
            {
                throw new NowClientException("The server negotiated a zero heartbeat interval.");
            }
            // The decoder accepts any uint; beyond the protocol's 24 h ceiling the doubled watchdog delay is invalid.
            if (capabilities.HeartbeatInterval > TimeSpan.FromHours(24))
            {
                throw new NowClientException("The server negotiated a heartbeat interval above 24 hours.");
            }
            return capabilities;
        }

        private static WorkerCtx CreateWorker(
            NowChannelTransport channel, NowMsgChannelCapset capabilities,
            DedicatedExecution? execution, CancellationToken cancellationToken)
        {
            // Negotiation successful
            Debug.WriteLine($"NOW channel negotiation complete");

            // client -> worker communication channel.
            // Reverse communication (e.g) worker -> client is done through passed handler objects.
            var clientChannel = Channel.CreateBounded<IClientCommand>(IoChannelCapacity);

            return new WorkerCtx
            {
                NowChannel = channel,
                Capabilities = capabilities,
                LastHeartbeat = Stopwatch.GetTimestamp(),
                Commands = clientChannel.Reader,
                CommandWriter = clientChannel.Writer,
                HeartbeatInterval = capabilities.HeartbeatInterval,
                Execution = execution,
                CancellationToken = cancellationToken,
            };
        }

        private static async Task RunWorker(WorkerCtx ctx)
        {
            try
            {
                await WorkerCtx.Run(ctx);
            }
            catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
            {
                // Lifetime cancellation is a normal local stop, not a protocol failure.
            }
            finally
            {
                ctx.CommandWriter.TryComplete();
            }
        }

        /// <summary>
        /// Send system shutdown command to the NOW-proto server.
        /// </summary>
        public async Task SystemShutdown(SystemShutdownParams shutdownParams)
        {
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
            ThrowIfWorkerTerminated();

            if (!Capabilities.SessionCapset.HasFlag(NowCapabilitySession.Msgbox))
            {
                ThrowCapabilitiesError("Message box");
            }

            var requestId = _nextMessageBoxId++;
            var message = msgBoxParams.ToNowMessage(requestId, true);
            var handler = new MessageBoxResponse(requestId, _lifetime.Token);
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
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
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
        public Task SessionSetKbdLayoutNext()
        {
            return SessionSetKbdLayoutImpl(NowMsgSessionSetKbdLayout.Next());
        }

        /// <summary>
        /// Set the previous keyboard layout for the active foreground window.
        /// </summary>
        public Task SessionSetKbdLayoutPrev()
        {
            return SessionSetKbdLayoutImpl(NowMsgSessionSetKbdLayout.Prev());
        }

        /// <summary>
        /// Set a specific keyboard layout for the active foreground window.
        /// </summary>
        public Task SessionSetKbdLayoutSpecific(string layout)
        {
            return SessionSetKbdLayoutImpl(NowMsgSessionSetKbdLayout.Specific(layout));
        }

        /// <summary>
        /// Sets the callback for window recording events.
        /// </summary>
        public async Task SetWindowRecEventHandler(IWindowRecEventHandler? handler)
        {
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
            ThrowIfWorkerTerminated();

            if (!Capabilities.ExecCapset.HasFlag(NowCapabilityExec.Process))
            {
                ThrowCapabilitiesError("Process execution style");
            }

            var sessionId = _nextExecSessionId++;
            var message = execParams.ToNowMessage(sessionId);
            var execSession = execParams.ToExecSession(sessionId, _commandWriter, _execution, _lifetime.Token);
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
            if (_execution != null)
                await _execution.Enter();
            ThrowIfWorkerTerminated();

            if (!Capabilities.ExecCapset.HasFlag(NowCapabilityExec.Shell))
            {
                ThrowCapabilitiesError("Shell execution style");
            }

            var sessionId = _nextExecSessionId++;
            var message = execParams.ToNowMessage(sessionId);
            var execSession = execParams.ToExecSession(sessionId, _commandWriter, _execution, _lifetime.Token);
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
            if (_execution != null)
                await _execution.Enter();
            ThrowIfWorkerTerminated();

            if (!Capabilities.ExecCapset.HasFlag(NowCapabilityExec.Batch))
            {
                ThrowCapabilitiesError("Batch execution style");
            }

            var sessionId = _nextExecSessionId++;
            var message = execParams.ToNowMessage(sessionId);
            var execSession = execParams.ToExecSession(sessionId, _commandWriter, _execution, _lifetime.Token);
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
            if (_execution != null)
                await _execution.Enter();
            ThrowIfWorkerTerminated();

            if (!Capabilities.ExecCapset.HasFlag(NowCapabilityExec.WinPs))
            {
                ThrowCapabilitiesError("Windows PowerShell execution style");
            }

            var sessionId = _nextExecSessionId++;
            var message = execParams.ToNowMessage(sessionId);
            var execSession = execParams.ToExecSession(sessionId, _commandWriter, _execution, _lifetime.Token);
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
            if (_execution != null)
                await _execution.Enter();
            ThrowIfWorkerTerminated();

            if (!Capabilities.ExecCapset.HasFlag(NowCapabilityExec.Pwsh))
            {
                ThrowCapabilitiesError("Pwsh execution style");
            }

            var sessionId = _nextExecSessionId++;
            var message = execParams.ToNowMessage(sessionId);
            var execSession = execParams.ToExecSession(sessionId, _commandWriter, _execution, _lifetime.Token);
            var command = new CommandExecPwsh(message, execSession);

            await _commandWriter.WriteAsync(command);

            return execSession;
        }

        /// <summary>
        /// Gracefully terminate and close the NOW-proto communication channel.
        /// </summary>
        public async Task ForceTerminate()
        {
            if (_execution != null)
                await _execution.Enter();
            await _commandWriter.WriteAsync(new CommandChannelClose());
        }

        /// <summary>
        /// Requests a local stop without waiting for a peer close response or a blocked write.
        /// Completes after protocol and transport cleanup. Does not synchronously join the worker.
        /// </summary>
        public Task StopAsync()
        {
            _lifetime.RequestStop();
            return Completion;
        }

        /// <summary>
        /// Stops the connection and releases its transport and owned execution resources.
        /// </summary>
        public ValueTask DisposeAsync() => new(StopAsync());

        /// <summary>
        /// Gracefully terminate and close the NOW-proto communication channel.
        /// </summary>
        [Obsolete("Use ForceTerminate instead.")]
        public Task ForceTermiate()
        {
            return ForceTerminate();
        }

        // RDM Methods

        /// <summary>
        /// Sets the callback for RDM application notifications.
        /// </summary>
        public async Task SetRdmAppNotifyHandler(RdmAppNotifyHandler? handler)
        {
            if (_execution != null)
                await _execution.Enter();
            ThrowIfWorkerTerminated();

            await _commandWriter.WriteAsync(new CommandSetRdmAppNotifyHandler(handler));
        }

        /// <summary>
        /// Sets the callback for RDM session notifications.
        /// </summary>
        public async Task SetRdmSessionNotifyHandler(RdmSessionNotifyHandler? handler)
        {
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
            {
                await _execution.Enter();
                ThrowIfWorkerTerminated();
                // Pool mode sends one exchange per caller; here concurrent callers share one exchange, which the
                // worker's send-order matching makes equivalent with fewer round-trips.
                var pending = _rdmSyncTask ??= RdmSyncCore();
                try
                {
                    await pending;
                }
                finally
                {
                    if (_rdmSyncTask == pending)
                        _rdmSyncTask = null;
                }
                return;
            }

            await RdmSyncCore();
        }

        private async Task RdmSyncCore()
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

            var response = await responseHandler.WaitForResponseAsync(TimeSpan.FromSeconds(10), _execution, _lifetime.Token);

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
            if (_execution != null)
                await _execution.Enter();
            await EnsureRdmCapabilitiesSent();

            return _rdmCapabilities?.IsAppAvailable ?? false;
        }

        /// <summary>
        /// Gets the RDM version information from the server.
        /// Automatically performs capabilities exchange if not already done.
        /// </summary>
        public async Task<RdmVersion?> GetRdmVersion()
        {
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
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
            if (_execution != null)
                await _execution.Enter();
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
            ChannelWriter<IClientCommand> commandWriter,
            DedicatedExecution? execution,
            ClientLifetime lifetime
        )
        {
            this.Capabilities = capabilities;
            this._runnerTask = workerTask;
            this._commandWriter = commandWriter;
            this._execution = execution;
            this._lifetime = lifetime;
        }

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

        /// <summary>
        /// Completes after worker, transport and owned execution cleanup. Protocol failures
        /// fault this task; local stop and lifetime cancellation complete it successfully.
        /// </summary>
        public Task Completion => _runnerTask;

        public NowMsgChannelCapset Capabilities { get; }

        private uint _nextMessageBoxId = 0;
        private uint _nextExecSessionId = 0;

        private readonly Task _runnerTask;
        private readonly ChannelWriter<IClientCommand> _commandWriter;
        private readonly DedicatedExecution? _execution;
        private readonly ClientLifetime _lifetime;
        private Task? _rdmSyncTask;
    }
}