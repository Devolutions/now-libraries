using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient
{
    /// <summary>
    /// Powershell 7 and higher (pwsh) remote execution session parameters.
    /// </summary>
    /// <param name="command">PowerShell command/script to execute.</param>
    public class ExecPwshParams(string command) : AExecParams
    {
        /// <summary>
        /// Create params for PowerShell server mode execution.
        /// </summary>
        public static ExecPwshParams NewServerMode()
        {
            var execParams = new ExecPwshParams("");
            execParams._serverMode = true;
            return execParams;
        }

        /// <summary>
        /// Set the working directory for the command/script.
        /// </summary>
        public ExecPwshParams Directory(string directory)
        {
            _directory = directory;
            return this;
        }

        /// <summary>
        /// Set the configuration name for the PowerShell session. (-ConfigurationName)
        /// </summary>
        public ExecPwshParams ConfigurationName(string configurationName)
        {
            _configurationName = configurationName;
            return this;
        }

        /// <summary>
        /// Set the execution policy for the PowerShell session. (-ExecutionPolicy)
        /// </summary>
        public ExecPwshParams ExecutionPolicy(string executionPolicy)
        {
            _executionPolicy = executionPolicy;
            return this;
        }

        /// <summary>
        /// Set the apartment state for the PowerShell session. (-Sta/-Mta)
        /// </summary>
        public ExecPwshParams ApartmentState(NowMsgExecPwsh.ApartmentStateKind apartmentState)
        {
            _apartmentState = apartmentState;
            return this;
        }

        /// <summary>
        /// Disable the PowerShell logo display. (-NoLogo)
        /// </summary>
        public ExecPwshParams NoLogo(bool enable = true)
        {
            _noLogo = enable;
            return this;
        }

        /// <summary>
        /// Do not close the PowerShell session after the command/script execution. (-NoExit)
        /// </summary>
        public ExecPwshParams NoExit(bool enable = true)
        {
            _noExit = enable;
            return this;
        }

        /// <summary>
        /// Do not load the PowerShell profile. (-NoProfile)
        /// </summary>
        public ExecPwshParams NoProfile(bool enable = true)
        {
            _noProfile = enable;
            return this;
        }

        /// <summary>
        /// Run the PowerShell session in non-interactive mode. (-NonInteractive)
        /// </summary>
        public ExecPwshParams NonInteractive(bool enable = true)
        {
            _nonInteractive = enable;
            return this;
        }

        /// <summary>
        /// Enables or disables the use of pipes for standard input, output, and error streams.
        /// When enabled, the process's standard streams are redirected through pipes.
        /// </summary>
        public ExecPwshParams IoRedirection(bool enable)
        {
            _ioRedirection = enable;
            return this;
        }

        /// <summary>
        /// Enables detached mode: PowerShell is started without tracking execution or sending back output.
        /// </summary>
        public ExecPwshParams Detached(bool enable = true)
        {
            _detached = enable;
            return this;
        }

        /// <summary>
        /// Disable default OEM-to-UTF-8 transcoding: pass raw bytes without encoding conversion.
        /// </summary>
        public ExecPwshParams RawEncoding(bool enable = true)
        {
            _rawEncoding = enable;
            return this;
        }

        /// <summary>
        /// Enable or disable Unicode console mode.
        /// </summary>
        public ExecPwshParams UnicodeConsole(bool enable = true)
        {
            _unicodeConsole = enable;
            return this;
        }

        internal NowMsgExecPwsh ToNowMessage(uint sessionId)
        {
            var builder = _serverMode
                ? NowMsgExecPwsh.Builder.NewCommandMode(sessionId)
                : new NowMsgExecPwsh.Builder(sessionId, command);

            if (_directory != null)
            {
                builder.Directory(_directory);
            }

            if (_configurationName != null)
            {
                builder.ConfigurationName(_configurationName);
            }

            if (_executionPolicy != null)
            {
                builder.ExecutionPolicy(_executionPolicy);
            }

            if (_apartmentState != null)
            {
                builder.ApartmentState(_apartmentState.Value);
            }

            if (_noLogo)
            {
                builder.SetNoLogo();
            }

            if (_noExit)
            {
                builder.SetNoExit();
            }

            if (_noProfile)
            {
                builder.SetNoProfile();
            }

            if (_nonInteractive)
            {
                builder.SetNonInteractive();
            }

            if (_ioRedirection)
            {
                builder.EnableIoRedirection();
            }

            if (_detached)
            {
                builder.EnableDetached();
            }

            if (_rawEncoding)
            {
                builder.EnableRawEncoding();
            }

            if (_unicodeConsole)
            {
                builder.EnableUnicodeConsole();
            }

            return builder.Build();
        }


        private string? _configurationName = null;
        private string? _executionPolicy = null;
        private string? _directory = null;
        private NowMsgExecPwsh.ApartmentStateKind? _apartmentState = null;
        private bool _noLogo = false;
        private bool _noExit = false;
        private bool _noProfile = false;
        private bool _nonInteractive = false;
        private bool _ioRedirection = false;
        private bool _detached = false;
        private bool _rawEncoding = false;
        private bool _unicodeConsole = false;
        private bool _serverMode = false;
    }
}