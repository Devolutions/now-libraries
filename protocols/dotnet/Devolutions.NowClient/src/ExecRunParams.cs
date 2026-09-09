using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient
{
    /// <summary>
    /// OS-specific simple "fire-and-forget" command execution parameters.
    /// Note that session state/result is not available for this type of execution.
    /// </summary>
    /// <param name="command">Command to execute.</param>
    public class ExecRunParams(string command) : AExecParams
    {
        /// <summary>
        /// Set the working directory for the command.
        /// </summary>
        public ExecRunParams Directory(string directory)
        {
            _directory = directory;
            return this;
        }

        /// <summary>
        /// Execute with elevated privileges. Requires the host to advertise an elevation
        /// capability; under shell-based elevation the session has no IO redirection.
        /// </summary>
        public ExecRunParams Elevated(bool enable = true)
        {
            IsElevated = enable;
            return this;
        }

        internal NowMsgExecRun ToNowMessage(uint sessionId)
        {
            var builder = new NowMsgExecRun.Builder(sessionId, command);

            if (_directory != null)
            {
                builder.Directory(_directory);
            }

            if (IsElevated)
            {
                builder.EnableElevated();
            }

            return builder.Build();
        }

        private string? _directory = null;
    }
}