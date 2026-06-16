using System.Diagnostics;
using System.Threading.Channels;

using Devolutions.NowClient.Worker;
using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient
{
    public delegate void StdoutHandler(uint sessionId, ArraySegment<byte> data, bool last);

    public delegate void StderrHandler(uint sessionId, ArraySegment<byte> data, bool last);

    public delegate void StartedHandler(uint sessionId);

    /// <summary>
    /// Active execution session handler.
    /// </summary>
    public class ExecSession : IExecSessionHandler
    {
        void IExecSessionHandler.HandleOutput(NowMsgExecData msg)
        {
            switch (msg.Stream)
            {
                case NowMsgExecData.StreamKind.Stdout when _onStdout != null:
                    _onStdout(SessionId, msg.Data, msg.Last);
                    return;
                case NowMsgExecData.StreamKind.Stderr when _onStderr != null:
                    _onStderr(SessionId, msg.Data, msg.Last);
                    return;
                case NowMsgExecData.StreamKind.Stdin:
                default:
                    Debug.WriteLine("Received unexpected output stream");
                    break;
            }
        }

        void IExecSessionHandler.HandleStarted()
        {
            _onStarted?.Invoke(SessionId);
        }

        void IExecSessionHandler.HandleCancelRsp(NowMsgExecCancelRsp msg)
        {
            if (!_cancelPending)
            {
                Debug.WriteLine("Received unexpected cancel response");
                return;
            }

            _cancelResponse = msg;
            _cancelReceived.Release();
        }

        void IExecSessionHandler.HandleResult(NowMsgExecResult msg)
        {
            _result = msg;
            _responseReceivedEvent.Release();
            if (_cancelPending)
            {
                _cancelReceived.Release();
            }
        }

        internal ExecSession(
            uint sessionId,
            ChannelWriter<IClientCommand> commandWriter,
            StdoutHandler? onStdout,
            StderrHandler? onStderr,
            StartedHandler? onStarted
        )
        {
            SessionId = sessionId;
            _onStdout = onStdout;
            _onStderr = onStderr;
            _onStarted = onStarted;
            _commandWriter = commandWriter;
        }

        /// <summary>
        /// Send an abort signal to the remote host with given exit code (if supported by OS).
        /// Successfully sent abort signal will mark the session as terminated both on
        /// server and client sides.
        /// </summary>
        public async Task Abort(uint exitCode)
        {
            ThrowIfExited();

            await _commandWriter.WriteAsync(new CommandExecAbort(SessionId, exitCode));
            _aborted = true;
            _cancelReceived.Release();
            _responseReceivedEvent.Release();
        }

        /// <summary>
        /// Send execution session cancel request to the remote host. This method will wait
        /// server response and throw an exception if the request has failed. Session is only
        /// considered cancelled if the server returned success response.
        /// </summary>
        public async Task Cancel()
        {
            ThrowIfExited();

            if (_cancelResponse != null)
            {
                _cancelResponse.ThrowIfError();
                return;
            }

            if (!_cancelPending)
            {
                _cancelPending = true;
                await _commandWriter.WriteAsync(new CommandExecCancel(SessionId));
            }

            await _cancelReceived.WaitAsync();

            if (_cancelResponse == null)
            {
                // Graceful session exit or abort could have happened in the meantime.
                return;
            }

            _cancelResponse.ThrowIfError();
        }

        /// <summary>
        /// Send stdin data to the remote host.
        /// </summary>
        public async Task SendStdin(ArraySegment<byte> data, bool last)
        {
            ThrowIfExited();

            if (_lastStdinSent)
            {
                throw new NowSessionException(SessionId, NowSessionException.NowSessionExceptionKind.StdinClosed);
            }

            if (last)
            {
                _lastStdinSent = true;
            }

            await _commandWriter.WriteAsync(new CommandExecData(SessionId, data, last));
        }

        /// <summary>
        /// Wait execution session to complete and return the exit code. Method will throw
        /// an exception if execution session has encountered an error. (non-zero exit
        /// codes are still considered as successful execution result)
        /// </summary>
        public async Task<uint> GetResult()
        {

            if (_result != null)
            {
                return _result.GetExitCodeOrThrow();
            }

            await _responseReceivedEvent.WaitAsync();

            return _aborted
                ? throw new NowSessionException(SessionId, NowSessionException.NowSessionExceptionKind.Terminated)
                : _result?.GetExitCodeOrThrow()
                  ?? throw new NowClientException("No result received");
        }

        /// <summary>
        /// Current session id.
        /// </summary>
        public uint SessionId { get; }

        private void ThrowIfExited()
        {
            if (_result != null || _aborted)
            {
                throw new NowSessionException(SessionId, NowSessionException.NowSessionExceptionKind.ExitedSessionInteraction);
            }
        }

        private bool _lastStdinSent;
        private bool _aborted;
        private bool _cancelPending;
        private NowMsgExecCancelRsp? _cancelResponse;
        private NowMsgExecResult? _result;

        private readonly SemaphoreSlim _cancelReceived = new(0, 1);
        private readonly SemaphoreSlim _responseReceivedEvent = new(0, 1);

        private readonly StdoutHandler? _onStdout;
        private readonly StderrHandler? _onStderr;
        private readonly StartedHandler? _onStarted;

        private readonly ChannelWriter<IClientCommand> _commandWriter;
    }
}