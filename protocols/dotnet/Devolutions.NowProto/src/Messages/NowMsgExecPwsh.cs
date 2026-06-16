using Devolutions.NowProto.Exceptions;
using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_EXEC_PWSH_MSG message is used to execute a remote Windows PowerShell (powershell.exe) command.
    ///
    /// NOW-PROTO: NOW_EXEC_PWSH_MSG
    /// </summary>
    public class NowMsgExecPwsh : INowSerialize, INowDeserialize<NowMsgExecPwsh>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassExec;
        public static byte TypeMessageKind => 0x15; // NOW-PROTO: NOW_EXEC_PWSH_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassExec;
        byte INowMessage.MessageKind => 0x15;

        // -- INowSerialize --

        ushort INowSerialize.Flags => (ushort)_flags;

        uint INowSerialize.BodySize => FixedPartSize
                                             + NowVarStr.LengthOf(Command)
                                             + NowVarStr.LengthOf(_directory)
                                             + NowVarStr.LengthOf(_executionPolicy)
                                             + NowVarStr.LengthOf(_configurationName);

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(SessionId);
            cursor.WriteVarStr(Command);
            cursor.WriteVarStr(_directory);
            cursor.WriteVarStr(_executionPolicy);
            cursor.WriteVarStr(_configurationName);
        }

        // -- INowDeserialize --

        static NowMsgExecPwsh INowDeserialize<NowMsgExecPwsh>.Deserialize(ushort flags, NowReadCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var msgFlags = (MsgFlags)flags;
            var sessionId = cursor.ReadUInt32Le();
            var command = cursor.ReadVarStr();
            var directory = cursor.ReadVarStr();
            var executionPolicy = cursor.ReadVarStr();
            var configurationName = cursor.ReadVarStr();

            var msg = new NowMsgExecPwsh
            {
                SessionId = sessionId,
                Command = command,
                _directory = directory,
                _executionPolicy = executionPolicy,
                _configurationName = configurationName,
                _flags = msgFlags,
            };

            return msg;
        }

        // -- impl --

        private const uint FixedPartSize = 4; // u32 sessionId

        [Flags]
        private enum MsgFlags
        {
            None = 0x0000,
            NoLogo = 0x0001,
            NoExit = 0x0002,
            Sta = 0x0004,
            Mta = 0x0008,
            NoProfile = 0x0010,
            NonInteractive = 0x0020,
            ExecutionPolicy = 0x0040,
            ConfigurationName = 0x0080,
            /// <summary>
            /// `directory` field contains non-default value and specifies command working directory.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_PS_DIRECTORY_SET
            /// </summary>
            DirectorySet = 0x0100,

            /// <summary>
            /// Disables default OEM-to-UTF-8 transcoding: data streams are passed through
            /// as raw bytes without any encoding conversion.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_PS_RAW_ENCODING
            /// </summary>
            RawEncoding = 0x0200,

            /// <summary>
            /// Enables Unicode (UTF-8) console mode: agent injects a UTF-8 encoding setup snippet
            /// at the beginning of the script, ensuring stdin/stdout/stderr use UTF-8.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_PS_UNICODE_CONSOLE
            /// </summary>
            UnicodeConsole = 0x0400,

            /// <summary>
            /// Enable stdio (stdout, stderr, stdin) redirection.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_PS_IO_REDIRECTION
            /// </summary>
            IoRedirection = 0x1000,

            /// <summary>
            /// Run PowerShell in server mode.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_PS_SERVER_MODE
            /// </summary>
            ServerMode = 0x2000,

            /// <summary>
            /// Detached mode: PowerShell is started without tracking execution or sending back output.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_PS_DETACHED
            /// </summary>
            Detached = 0x8000,
        }

        public enum ApartmentStateKind : ushort
        {
            Sta,
            Mta,
        }

        /// <summary>
        /// Hide public default constructor.
        /// </summary>
        internal NowMsgExecPwsh() { }

        public class Builder(uint sessionId, string command)
        {
            public static Builder NewCommandMode(uint sessionId)
            {
                var builder = new Builder(sessionId, "");
                builder._flags |= MsgFlags.ServerMode;

                return builder;
            }
            public Builder Directory(string directory)
            {
                _directory = directory;
                _flags |= MsgFlags.DirectorySet;
                return this;
            }

            public Builder SetNoLogo()
            {
                _flags |= MsgFlags.NoLogo;
                return this;
            }

            public Builder SetNoExit()
            {
                _flags |= MsgFlags.NoExit;
                return this;
            }

            public Builder ApartmentState(ApartmentStateKind state)
            {
                _flags |= state switch
                {
                    ApartmentStateKind.Sta => MsgFlags.Sta,
                    ApartmentStateKind.Mta => MsgFlags.Mta,
                    _ => throw new NowEncodeException(NowEncodeException.ErrorKind.InvalidApartmentState),
                };

                return this;
            }

            public Builder SetNoProfile()
            {
                _flags |= MsgFlags.NoProfile;
                return this;
            }

            public Builder SetNonInteractive()
            {
                _flags |= MsgFlags.NonInteractive;
                return this;
            }

            public Builder ExecutionPolicy(string policy)
            {
                _executionPolicy = policy;
                _flags |= MsgFlags.ExecutionPolicy;
                return this;
            }

            public Builder ConfigurationName(string name)
            {
                _configurationName = name;
                _flags |= MsgFlags.ConfigurationName;
                return this;
            }
            public Builder EnableIoRedirection()
            {
                _flags |= MsgFlags.IoRedirection;
                return this;
            }

            public Builder EnableRawEncoding()
            {
                _flags |= MsgFlags.RawEncoding;
                return this;
            }

            public Builder EnableUnicodeConsole()
            {
                _flags |= MsgFlags.UnicodeConsole;
                return this;
            }

            public Builder EnableDetached()
            {
                _flags |= MsgFlags.Detached;
                return this;
            }

            public NowMsgExecPwsh Build()
            {
                return new NowMsgExecPwsh
                {
                    SessionId = sessionId,
                    Command = command,
                    _flags = _flags,
                    _directory = _directory,
                    _executionPolicy = _executionPolicy,
                    _configurationName = _configurationName,
                };
            }

            private MsgFlags _flags = MsgFlags.None;
            private string _directory = "";
            private string _executionPolicy = "";
            private string _configurationName = "";
        }

        /// <summary>
        /// PowerShell -NoLogo option.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PS_NO_LOGO
        /// </summary>
        public bool NoLogo => _flags.HasFlag(MsgFlags.NoLogo);

        /// <summary>
        /// PowerShell -NoExit option.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PS_NO_EXIT
        /// </summary>
        public bool NoExit => _flags.HasFlag(MsgFlags.NoExit);


        /// <summary>
        /// PowerShell -Mta & -Sta options
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PS_MTA/NOW_EXEC_FLAG_PS_STA
        /// </summary>
        public ApartmentStateKind? ApartmentState
        {
            get
            {
                var sta = _flags.HasFlag(MsgFlags.Sta);
                var mta = _flags.HasFlag(MsgFlags.Mta);
                if (sta && mta)
                {
                    throw new NowDecodeException(NowDecodeException.ErrorKind.InvalidApartmentStateFlags);
                }

                if (!(sta || mta))
                {
                    // Not specified
                    return null;
                }

                return sta ? ApartmentStateKind.Sta : ApartmentStateKind.Mta;
            }
        }

        /// <summary>
        /// PowerShell -NoProfile option.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PS_NO_PROFILE
        /// </summary>
        public bool NoProfile => _flags.HasFlag(MsgFlags.NoProfile);

        /// <summary>
        /// PowerShell -NonInteractive option.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PS_NON_INTERACTIVE
        /// </summary>
        public bool NonInteractive => _flags.HasFlag(MsgFlags.NonInteractive);

        /// <summary>
        /// The PowerShell -ExecutionPolicy parameter.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PS_EXECUTION_POLICY
        /// </summary>
        public string? ExecutionPolicy => _flags.HasFlag(MsgFlags.ExecutionPolicy)
            ? _executionPolicy
            : null;

        /// <summary>
        /// The PowerShell -ConfigurationName parameter.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_PS_CONFIGURATION_NAME
        /// </summary>
        public string? ConfigurationName => _flags.HasFlag(MsgFlags.ConfigurationName)
            ? _configurationName
            : null;

        /// <summary>
        /// Command execution directory.
        /// </summary>
        public string? Directory => _flags.HasFlag(MsgFlags.DirectorySet)
            ? _directory
            : null;

        public uint SessionId { get; private init; } = 0;
        public string Command { get; private init; } = "";

        public bool IoRedirection => _flags.HasFlag(MsgFlags.IoRedirection);
        public bool RawEncoding => _flags.HasFlag(MsgFlags.RawEncoding);
        public bool UnicodeConsole => _flags.HasFlag(MsgFlags.UnicodeConsole);
        public bool ServerMode => _flags.HasFlag(MsgFlags.ServerMode);
        public bool Detached => _flags.HasFlag(MsgFlags.Detached);

        private MsgFlags _flags = MsgFlags.None;
        private string _directory = "";
        private string _executionPolicy = "";
        private string _configurationName = "";
    }
}