using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_EXEC_BATCH_MSG message is used to execute a remote batch command.
    ///
    /// NOW-PROTO: NOW_EXEC_BATCH_MSG
    /// </summary>
    public class NowMsgExecBatch : INowSerialize, INowDeserialize<NowMsgExecBatch>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassExec;
        public static byte TypeMessageKind => 0x13; // NOW-PROTO: NOW_EXEC_BATCH_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassExec;
        byte INowMessage.MessageKind => 0x13;

        // -- INowSerialize --

        ushort INowSerialize.Flags => (ushort)(
            (Directory != null ? MsgFlags.DirectorySet : 0) |
            (RawEncoding ? MsgFlags.RawEncoding : 0) |
            (UnicodeConsole ? MsgFlags.UnicodeConsole : 0) |
            (IoRedirection ? MsgFlags.IoRedirection : 0) |
            (Detached ? MsgFlags.Detached : 0)
        );
        uint INowSerialize.BodySize =>
            FixedPartSize
            + NowVarStr.LengthOf(Filename)
            + NowVarStr.LengthOf(Directory ?? "");

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(SessionId);
            cursor.WriteVarStr(Filename);
            cursor.WriteVarStr(Directory ?? "");
        }

        // -- INowDeserialize --
        static NowMsgExecBatch INowDeserialize<NowMsgExecBatch>.Deserialize(ushort flags, NowReadCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var msgFlags = (MsgFlags)flags;
            var sessionId = cursor.ReadUInt32Le();
            var filename = cursor.ReadVarStr();
            var directory = cursor.ReadVarStr();

            return new NowMsgExecBatch(
                sessionId,
                filename,
                msgFlags.HasFlag(MsgFlags.DirectorySet) ? directory : null,
                msgFlags.HasFlag(MsgFlags.RawEncoding),
                msgFlags.HasFlag(MsgFlags.UnicodeConsole),
                msgFlags.HasFlag(MsgFlags.IoRedirection),
                msgFlags.HasFlag(MsgFlags.Detached)
            );
        }

        // -- impl --

        [Flags]
        private enum MsgFlags : ushort
        {
            /// <summary>
            /// Set if directory field contains non-default value.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_BATCH_DIRECTORY_SET
            /// </summary>
            DirectorySet = 0x0001,

            /// <summary>
            /// Disables default OEM-to-UTF-8 transcoding: data streams are passed through
            /// as raw bytes without any encoding conversion.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_BATCH_RAW_ENCODING
            /// </summary>
            RawEncoding = 0x0002,

            /// <summary>
            /// Enables Unicode (UTF-8) console mode: agent injects `@chcp 65001 > nul`
            /// and writes the script in BOM-less UTF-8 encoding.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_BATCH_UNICODE_CONSOLE
            /// </summary>
            UnicodeConsole = 0x0004,

            /// <summary>
            /// Enable stdio (stdout, stderr, stdin) redirection.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_BATCH_IO_REDIRECTION
            /// </summary>
            IoRedirection = 0x1000,

            /// <summary>
            /// Detached mode: the batch is started without tracking execution or sending back output.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_BATCH_DETACHED
            /// </summary>
            Detached = 0x8000,
        }

        private const uint FixedPartSize = 4; // u32 SessionId

        public class Builder(uint sessionId, string filename)
        {
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

            public Builder EnableRawEncoding()
            {
                _rawEncoding = true;
                return this;
            }

            public Builder EnableUnicodeConsole()
            {
                _unicodeConsole = true;
                return this;
            }

            public Builder EnableDetached()
            {
                _detached = true;
                return this;
            }

            public NowMsgExecBatch Build()
            {
                return new NowMsgExecBatch(_sessionId, _filename, _directory, _rawEncoding, _unicodeConsole, _ioRedirection, _detached);
            }

            private readonly uint _sessionId = sessionId;
            private readonly string _filename = filename;
            private string? _directory = null;
            private bool _rawEncoding = false;
            private bool _unicodeConsole = false;
            private bool _ioRedirection = false;
            private bool _detached = false;
        }

        internal NowMsgExecBatch(uint sessionId, string filename, string? directory, bool rawEncoding, bool unicodeConsole, bool ioRedirection, bool detached)
        {
            SessionId = sessionId;
            Filename = filename;
            Directory = directory;
            RawEncoding = rawEncoding;
            UnicodeConsole = unicodeConsole;
            IoRedirection = ioRedirection;
            Detached = detached;
        }

        public uint SessionId { get; }
        public string Filename { get; }
        public string? Directory { get; }
        public bool RawEncoding { get; }
        public bool UnicodeConsole { get; }
        public bool IoRedirection { get; }
        public bool Detached { get; }
    }
}