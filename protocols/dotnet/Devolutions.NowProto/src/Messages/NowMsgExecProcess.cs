using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_EXEC_PROCESS_MSG message is used to send a Windows CreateProcess() request.
    ///
    /// NOW-PROTO: NOW_EXEC_PROCESS_MSG
    /// </summary>
    public class NowMsgExecProcess : INowSerialize, INowDeserialize<NowMsgExecProcess>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassExec;
        public static byte TypeMessageKind => 0x11; // NOW-PROTO: NOW_EXEC_PROCESS_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassExec;
        byte INowMessage.MessageKind => 0x11;

        // -- INowSerialize --

        ushort INowSerialize.Flags => (ushort)(
            (Parameters != null ? MsgFlags.ParametersSet : 0)
            | (Directory != null ? MsgFlags.DirectorySet : 0)
            | (EncodingUtf8 ? MsgFlags.EncodingUtf8 : 0)
            | (IoRedirection ? MsgFlags.IoRedirection : 0)
            | (Detached ? MsgFlags.Detached : 0)
        );
        uint INowSerialize.BodySize =>
            FixedPartSize
            + NowVarStr.LengthOf(Filename)
            + NowVarStr.LengthOf(Parameters ?? "")
            + NowVarStr.LengthOf(Directory ?? "");

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(SessionId);
            cursor.WriteVarStr(Filename);
            cursor.WriteVarStr(Parameters ?? "");
            cursor.WriteVarStr(Directory ?? "");
        }

        // -- INowDeserialize --
        static NowMsgExecProcess INowDeserialize<NowMsgExecProcess>.Deserialize(ushort flags, NowReadCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var msgFlags = (MsgFlags)flags;
            var sessionId = cursor.ReadUInt32Le();
            var filename = cursor.ReadVarStr();
            var parameters = cursor.ReadVarStr();
            var directory = cursor.ReadVarStr();

            return new NowMsgExecProcess(
                sessionId,
                filename,
                msgFlags.HasFlag(MsgFlags.ParametersSet) ? parameters : null,
                msgFlags.HasFlag(MsgFlags.DirectorySet) ? directory : null,
                msgFlags.HasFlag(MsgFlags.EncodingUtf8),
                msgFlags.HasFlag(MsgFlags.IoRedirection),
                msgFlags.HasFlag(MsgFlags.Detached)
            );
        }

        // -- impl --

        [Flags]
        private enum MsgFlags : ushort
        {
            /// <summary>
            /// Set if parameters field contains non-default value.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_PROCESS_PARAMETERS_SET
            /// </summary>
            ParametersSet = 0x0001,

            /// <summary>
            /// Set if directory field contains non-default value.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_PROCESS_DIRECTORY_SET
            /// </summary>
            DirectorySet = 0x0002,

            /// <summary>
            /// Enables OEM-to-UTF-8 transcoding for stdin, stdout, and stderr.
            /// Without this flag, data streams are passed through as raw bytes.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_PROCESS_ENCODING_UTF8
            /// </summary>
            EncodingUtf8 = 0x0004,

            /// <summary>
            /// Enable stdio (stdout, stderr, stdin) redirection.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_PROCESS_IO_REDIRECTION
            /// </summary>
            IoRedirection = 0x1000,

            /// <summary>
            /// Detached mode: the process is started without tracking execution or sending back output.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_PROCESS_DETACHED
            /// </summary>
            Detached = 0x8000,
        }

        private const uint FixedPartSize = 4; // u32 SessionId

        public class Builder(uint sessionId, string filename)
        {
            public Builder Parameters(string parameters)
            {
                _parameters = parameters;
                return this;
            }

            public Builder Directory(string directory)
            {
                _directory = directory;
                return this;
            }

            public Builder EnableIoRedirection()
            {
                _ioRedirection = true;
                return this;
            }

            public Builder EnableEncodingUtf8()
            {
                _encodingUtf8 = true;
                return this;
            }

            public Builder EnableDetached()
            {
                _detached = true;
                return this;
            }

            public NowMsgExecProcess Build()
            {
                return new NowMsgExecProcess(_sessionId, _filename, _parameters, _directory, _encodingUtf8, _ioRedirection, _detached);
            }

            private readonly uint _sessionId = sessionId;
            private readonly string _filename = filename;
            private string? _parameters = null;
            private string? _directory = null;
            private bool _encodingUtf8 = false;
            private bool _ioRedirection = false;
            private bool _detached = false;
        }

        internal NowMsgExecProcess(uint sessionId, string filename, string? parameters, string? directory, bool encodingUtf8, bool ioRedirection, bool detached)
        {
            SessionId = sessionId;
            Filename = filename;
            Parameters = parameters;
            Directory = directory;
            EncodingUtf8 = encodingUtf8;
            IoRedirection = ioRedirection;
            Detached = detached;
        }

        public uint SessionId { get; }
        public string Filename { get; }
        public string? Parameters { get; }
        public string? Directory { get; }
        public bool EncodingUtf8 { get; }
        public bool IoRedirection { get; }
        public bool Detached { get; }
    }
}