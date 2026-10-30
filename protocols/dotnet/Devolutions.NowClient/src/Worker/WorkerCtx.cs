using System.Diagnostics;
using System.Threading.Channels;

using Devolutions.NowClient.Execution;
using Devolutions.NowProto;
using Devolutions.NowProto.Exceptions;
using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    /// <summary>
    /// Background worker logic for NowClient.
    /// </summary>
    internal class WorkerCtx
    {
        public static async Task Run(WorkerCtx ctx)
        {
            try
            {
                await RunImpl(ctx);
            }
            finally
            {
                // Cleanup any pending handlers before exiting
                try
                {
                    ctx.CleanupPendingHandlers();
                }
                finally
                {
                    ctx.NowChannel.Dispose();
                }
            }
        }

        private static async Task RunImpl(WorkerCtx ctx)
        {
            using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
            try
            {
                await RunLoop(ctx, heartbeatStop.Token);
            }
            finally
            {
                heartbeatStop.Cancel();
            }
        }

        private static async Task<IClientCommand> ReadCommand(WorkerCtx ctx)
        {
            // Capture the protocol context before racing a channel's asynchronous completion.
            return await ctx.Commands.ReadAsync(ctx.CancellationToken);
        }

        private static async Task RunLoop(WorkerCtx ctx, CancellationToken heartbeatToken)
        {
            var reads = new PendingReads();
            try
            {
                await RunSelectLoop(ctx, heartbeatToken, reads);
            }
            finally
            {
                // Whatever ended the loop, the losing reads are still pending; completing the command writer and
                // disposing the transport then faults them with nobody awaiting.
                if (reads.Client != null)
                    ProtocolTasks.ObserveFault(reads.Client);
                if (reads.Server != null)
                    ProtocolTasks.ObserveFault(reads.Server);
            }
        }

        private sealed class PendingReads
        {
            public Task<IClientCommand>? Client;
            public Task<NowMessage.NowMessageView>? Server;
        }

        private static async Task RunSelectLoop(WorkerCtx ctx, CancellationToken heartbeatToken, PendingReads reads)
        {
            Task? heartbeatCheckTask = null;
            bool preferServer = true;

            var tasks = new List<Task>();

            // Main async IO loop. (akin to tokio's `select!`)
            while (!ctx.ExitRequested)
            {
                // Even an always-ready transport/command queue must let deadlines and stop
                // requests run on the dedicated pump.
                if (ctx.Execution != null)
                    await Task.Yield();
                ctx.CancellationToken.ThrowIfCancellationRequested();
                tasks.Clear();

                // Check if task was completed on the previous loop iteration.
                // and re-add it to the list of tasks to be awaited.
                reads.Client ??= ReadCommand(ctx);
                reads.Server ??= ctx.NowChannel.ReadMessageAny();
                var clientReadTask = reads.Client;
                var serverReadTask = reads.Server;

                // Skip task of heartbeat interval was not negotiated.
                if (heartbeatCheckTask == null && ctx.HeartbeatInterval != null)
                {
                    heartbeatCheckTask = ctx.Execution != null
                        ? ctx.Execution.Delay(ctx.HeartbeatInterval.Value * 2, heartbeatToken)
                        : Task.Delay(ctx.HeartbeatInterval.Value * 2, heartbeatToken);
                }

                // Give a received heartbeat one turn before checking expiry, but do not let
                // continuously ready I/O starve the timer (or either direction of I/O).
                var checkHeartbeatFirst = heartbeatCheckTask?.IsCompleted == true
                    && (!serverReadTask.IsCompleted || !preferServer);
                if (checkHeartbeatFirst)
                    tasks.Add(heartbeatCheckTask!);
                if (preferServer)
                {
                    tasks.Add(serverReadTask);
                    tasks.Add(clientReadTask);
                }
                else
                {
                    tasks.Add(clientReadTask);
                    tasks.Add(serverReadTask);
                }
                if (heartbeatCheckTask != null && !checkHeartbeatFirst)
                    tasks.Add(heartbeatCheckTask);

                var completedTask = await Task.WhenAny(tasks);

                if (completedTask == clientReadTask)
                {
                    var command = await clientReadTask;
                    preferServer = true;
                    reads.Client = null;
                    // See concrete commands implementations in
                    // Devolutions.NowClient.Worker namespace.
                    await command.Execute(ctx);
                }
                else if (completedTask == serverReadTask)
                {
                    var message = await serverReadTask;
                    preferServer = false;
                    reads.Server = null;
                    // Deployed Agents only heartbeat after a full interval of silence, so any message is proof of life.
                    ctx.LastReceived = Stopwatch.GetTimestamp();

                    switch (message.MessageClass)
                    {
                        case NowMessage.ClassChannel:
                            HandleChannelMessage(message, ctx);
                            break;
                        case NowMessage.ClassSystem:
                            // System messages are not expected to have responses(yet).
                            Debug.WriteLine($"Unhandled system message kind={message.MessageKind}");
                            break;
                        case NowMessage.ClassSession:
                            HandleSessionMessage(message, ctx);
                            break;
                        case NowMessage.ClassExec:
                            HandleExecMessage(message, ctx);
                            break;
                        case NowMessage.ClassRdm:
                            HandleRdmMessage(message, ctx);
                            break;
                        default:
                            Debug.WriteLine($"Unhandled message class={message.MessageClass}");
                            break;
                    }
                }
                else if (completedTask == heartbeatCheckTask)
                {
                    var heartbeatLeeway = TimeSpan.FromSeconds(5);

                    heartbeatCheckTask = null;
                    if (!(Stopwatch.GetElapsedTime(ctx.LastReceived) > (ctx.HeartbeatInterval + heartbeatLeeway)))
                    {
                        continue;
                    }

                    Debug.WriteLine("Heartbeat timeout triggered");

                    // Channel is considered dead; No attempt to send any messages should be made.
                    ctx.ExitRequested = true;
                }
            }
        }

        private static void HandleChannelMessage(NowMessage.NowMessageView message, WorkerCtx ctx)
        {
            var kind = (ChannelMessageKind)message.MessageKind;

            switch (kind)
            {
                case ChannelMessageKind.Heartbeat:
                    break;
                case ChannelMessageKind.Close:
                    var decoded = message.Deserialize<NowMsgChannelClose>();
                    ctx.ExitRequested = true;
                    try
                    {
                        decoded.ThrowIfError();
                    }
                    catch (NowException e)
                    {
                        Debug.WriteLine($"Channel close error: {e.Message}");
                    }

                    break;
                default:
                    Debug.WriteLine($"Unhandled channel message kind={message.MessageKind}");
                    break;
            }
        }

        private static void HandleSessionMessage(NowMessage.NowMessageView message, WorkerCtx ctx)
        {
            var kind = (SessionMessageKind)message.MessageKind;

            Debug.WriteLine($"Received session message");

            switch (kind)
            {
                case SessionMessageKind.MsgBoxRsp:
                    var decoded = message.Deserialize<NowMsgSessionMessageBoxRsp>();

                    if (ctx.MessageBoxHandlers.TryGetValue(decoded.RequestId, out IMessageBoxRspHandler? handler))
                    {
                        handler.HandleMessageBoxRsp(decoded);
                        ctx.MessageBoxHandlers.Remove(decoded.RequestId);
                    }
                    else
                    {
                        Debug.WriteLine($"Received unexpected message box response with requestId={decoded.RequestId}");
                    }

                    break;
                case SessionMessageKind.WindowRecEvent:
                    var windowRecEvent = message.Deserialize<NowMsgSessionWindowRecEvent>();

                    if (ctx.WindowRecEventHandler != null)
                    {
                        ctx.WindowRecEventHandler.HandleWindowRecEvent(windowRecEvent);
                    }
                    else
                    {
                        Debug.WriteLine($"Received window recording event but no handler is registered");
                    }

                    break;
                default:
                    Debug.WriteLine($"Unhandled session message kind: {message.MessageKind}");
                    break;
            }
        }

        private static void HandleExecMessage(NowMessage.NowMessageView message, WorkerCtx ctx)
        {
            var kind = (ExecMessageKind)message.MessageKind;

            switch (kind)
            {
                case ExecMessageKind.CancelRsp:
                    {
                        var decoded = message.Deserialize<NowMsgExecCancelRsp>();

                        if (ctx.ExecSessionHandlers.TryGetValue(decoded.SessionId, out IExecSessionHandler? handler))
                        {
                            handler.HandleCancelRsp(decoded);
                        }
                        else
                        {
                            Debug.WriteLine(
                                $"Received unexpected exec cancel response with sessionId={decoded.SessionId}");
                        }

                        break;
                    }
                case ExecMessageKind.Data:
                    {
                        var decoded = message.Deserialize<NowMsgExecData>();
                        if (ctx.ExecSessionHandlers.TryGetValue(decoded.SessionId, out IExecSessionHandler? handler))
                        {
                            handler.HandleOutput(decoded);
                        }
                        else
                        {
                            Debug.WriteLine($"Received unexpected exec data with sessionId={decoded.SessionId}");
                        }

                        break;
                    }
                case ExecMessageKind.Started:
                    {
                        var decoded = message.Deserialize<NowMsgExecStarted>();
                        if (ctx.ExecSessionHandlers.TryGetValue(decoded.SessionId, out IExecSessionHandler? handler))
                        {
                            handler.HandleStarted(decoded);
                        }
                        else
                        {
                            Debug.WriteLine($"Received unexpected exec started with sessionId={decoded.SessionId}");
                        }

                        break;
                    }
                case ExecMessageKind.Result:
                    {
                        var decoded = message.Deserialize<NowMsgExecResult>();
                        if (ctx.ExecSessionHandlers.TryGetValue(decoded.SessionId, out IExecSessionHandler? handler))
                        {
                            handler.HandleResult(decoded);
                            // Unregister session after receiving the result.
                            ctx.ExecSessionHandlers.Remove(decoded.SessionId);
                        }
                        else
                        {
                            Debug.WriteLine($"Received unexpected exec result with sessionId={decoded.SessionId}");
                        }

                        break;
                    }
                default:
                    Debug.WriteLine($"Unhandled exec message kind: {message.MessageKind}");
                    break;
            }
        }

        private static void HandleRdmMessage(NowMessage.NowMessageView message, WorkerCtx ctx)
        {
            var kind = (RdmMessageKind)message.MessageKind;

            switch (kind)
            {
                case RdmMessageKind.Capabilities:
                    var capabilities = message.Deserialize<NowMsgRdmCapabilities>();
                    ctx.HandleRdmCapabilitiesResponse(capabilities);
                    break;

                case RdmMessageKind.AppNotify:
                    var appNotify = message.Deserialize<NowMsgRdmAppNotify>();
                    ctx.RdmAppNotifyHandler?.Invoke(appNotify.AppState, appNotify.ReasonCode, appNotify.NotifyData);
                    break;

                case RdmMessageKind.SessionNotify:
                    var sessionNotify = message.Deserialize<NowMsgRdmSessionNotify>();
                    ctx.HandleRdmSessionNotify(sessionNotify);
                    break;

                default:
                    Debug.WriteLine($"Unhandled RDM message kind: {message.MessageKind}");
                    break;
            }
        }

        // -- IO --
        public required NowChannelTransport NowChannel;
        public required ChannelReader<IClientCommand> Commands;
        public required ChannelWriter<IClientCommand> CommandWriter;
        public DedicatedExecution? Execution;
        public CancellationToken CancellationToken;

        // -- State --
        public required TimeSpan? HeartbeatInterval;
        public required long LastReceived;
        public required NowMsgChannelCapset Capabilities;
        public bool ExitRequested = false;

        public Dictionary<uint, IMessageBoxRspHandler> MessageBoxHandlers = [];
        public Dictionary<uint, IExecSessionHandler> ExecSessionHandlers = [];

        // -- Session State --
        public IWindowRecEventHandler? WindowRecEventHandler;

        // -- RDM State --
        public RdmAppNotifyHandler? RdmAppNotifyHandler;
        public RdmSessionNotifyHandler? RdmSessionNotifyHandler;

        // Capability responses carry no request id, so they are matched to requests in send order; the Agent answers
        // each one inline, in order. A request that timed out stays queued and consumes its own late response
        // instead of completing the next exchange. The flip side is that a request the peer never answers (its
        // error path sends nothing, or it dropped the message under overload) shifts every later response by one;
        // the client cannot tell late from lost, so only a reconnect recovers.
        private readonly Queue<RdmCapabilitiesResponseHandler> _rdmCapabilitiesHandlers = new();

        public void RegisterRdmCapabilitiesHandler(RdmCapabilitiesResponseHandler handler)
        {
            _rdmCapabilitiesHandlers.Enqueue(handler);
        }

        private void HandleRdmCapabilitiesResponse(NowMsgRdmCapabilities response)
        {
            if (_rdmCapabilitiesHandlers.TryDequeue(out var handler))
            {
                handler.SetResponse(response);
            }
        }

        public void HandleRdmSessionNotify(NowMsgRdmSessionNotify sessionNotify)
        {
            // Invoke the global notify handler if registered
            RdmSessionNotifyHandler?.Invoke(sessionNotify.SessionId, sessionNotify.SessionNotifyKind, sessionNotify.LogData);
        }

        /// <summary>
        /// Cleanup method to handle pending RDM capabilities handlers when worker exits
        /// </summary>
        public void CleanupPendingHandlers()
        {
            while (_rdmCapabilitiesHandlers.TryDequeue(out var handler))
            {
                handler.SetException(new OperationCanceledException("Worker terminated"));
            }
        }
    }
}