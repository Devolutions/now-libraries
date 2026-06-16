using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient
{
    /// <summary>
    /// Plain process execution session parameters (e.g. CreateProcessW on Windows hosts).
    /// </summary>
    /// <param name="filename"></param>
    public class ExecProcessParams(string filename) : AExecParams
    {
        /// <summary>
        /// Set the command line parameters for the process.
        /// </summary>
        public ExecProcessParams Parameters(string parameters)
        {
            _parameters = parameters;
            return this;
        }

        /// <summary>
        /// Set the working directory for the process.
        /// </summary>
        public ExecProcessParams Directory(string directory)
        {
            _directory = directory;
            return this;
        }

        /// <summary>
        /// Enables or disables the use of pipes for standard input, output, and error streams.
        /// When enabled, the process's standard streams are redirected through pipes.
        /// </summary>
        public ExecProcessParams IoRedirection(bool enable)
        {
            _ioRedirection = enable;
            return this;
        }

        /// <summary>
        /// Enables detached mode: the process is started without tracking execution or sending back output.
        /// </summary>
        public ExecProcessParams Detached(bool enable = true)
        {
            _detached = enable;
            return this;
        }

        /// <summary>
        /// Enable OEM-to-UTF-8 transcoding for stdin/stdout/stderr. Without this, data is passed through as raw bytes.
        /// </summary>
        public ExecProcessParams EncodingUtf8(bool enable = true)
        {
            _encodingUtf8 = enable;
            return this;
        }

        internal NowMsgExecProcess ToNowMessage(uint sessionId)
        {
            var builder = new NowMsgExecProcess.Builder(sessionId, filename);

            if (_parameters != null)
            {
                builder.Parameters(_parameters);
            }

            if (_directory != null)
            {
                builder.Directory(_directory);
            }

            if (_ioRedirection)
            {
                builder.EnableIoRedirection();
            }

            if (_detached)
            {
                builder.EnableDetached();
            }

            if (_encodingUtf8)
            {
                builder.EnableEncodingUtf8();
            }

            return builder.Build();
        }


        private bool _ioRedirection = false;
        private bool _detached = false;
        private bool _encodingUtf8 = false;
        private string? _parameters = null;
        private string? _directory = null;
    }
}