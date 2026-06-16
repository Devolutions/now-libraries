using System.Threading.Channels;

using Devolutions.NowClient.Worker;

namespace Devolutions.NowClient
{
    /// <summary>
    /// Common remote execution parameters for all execution styles.
    /// </summary>
    public abstract class AExecParams
    {
        /// <summary>
        /// Callback for stdout data collection.
        /// </summary>
        public StdoutHandler OnStdout
        {
            set
            {
                _stdoutHandler = value;
            }
        }

        /// <summary>
        /// Callback for stderr data collection.
        /// </summary>
        public StderrHandler OnStderr
        {
            set
            {
                _stderrHandler = value;
            }
        }

        /// <summary>
        /// Callback to be called when the execution session starts on the remote host.
        /// </summary>
        public StartedHandler OnStarted
        {
            set
            {
                _startedHandler = value;
            }
        }

        internal ExecSession ToExecSession(uint sessionId, ChannelWriter<IClientCommand> commandWriter)
        {
            return new ExecSession(
                sessionId,
                commandWriter,
                _stdoutHandler,
                _stderrHandler,
                _startedHandler
            );
        }

        private StdoutHandler? _stdoutHandler;
        private StderrHandler? _stderrHandler;
        private StartedHandler? _startedHandler;
    }
}