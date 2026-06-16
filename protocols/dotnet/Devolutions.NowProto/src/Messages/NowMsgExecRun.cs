using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_EXEC_RUN_MSG message is used to send a run request. This request type maps to starting
    /// a program by using the “Run” menu on operating systems (the Start Menu on Windows, the Dock on
    /// macOS etc.). The execution of programs started with NOW_EXEC_RUN_MSG is not followed and does
    /// not send back the output.
    ///
    /// NOW_PROTO: NOW_EXEC_RUN_MSG
    /// </summary>
    public class NowMsgExecRun : INowSerialize, INowDeserialize<NowMsgExecRun>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassExec;
        public static byte TypeMessageKind => 0x10; // NOW-PROTO: NOW_EXEC_RUN_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassExec;
        byte INowMessage.MessageKind => 0x10;

        // -- INowSerialize --

        ushort INowSerialize.Flags => (ushort)(
            !string.IsNullOrEmpty(Directory) ? MsgFlags.DirectorySet : 0
        );

        uint INowSerialize.BodySize => FixedPartSize
            + NowVarStr.LengthOf(Command)
            + NowVarStr.LengthOf(Directory ?? string.Empty);

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(SessionId);
            cursor.WriteVarStr(Command);
            cursor.WriteVarStr(Directory ?? string.Empty);
        }

        // -- INowDeserialize --

        static NowMsgExecRun INowDeserialize<NowMsgExecRun>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);

            var sessionId = cursor.ReadUInt32Le();
            var command = cursor.ReadVarStr();

            string? directory = null;
            if (!cursor.IsEmpty())
            {
                directory = cursor.ReadVarStr();
            }

            return new NowMsgExecRun
            {
                SessionId = sessionId,
                Command = command,
                Directory = directory
            };
        }

        // -- impl --

        public NowMsgExecRun(uint sessionId, string command)
        {
            SessionId = sessionId;
            Command = command;
            Directory = null;
        }

        private NowMsgExecRun()
        {
            SessionId = 0;
            Command = string.Empty;
            Directory = null;
        }

        [Flags]
        private enum MsgFlags : ushort
        {
            /// <summary>
            /// Set if directory field contains non-default value.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_RUN_DIRECTORY_SET
            /// </summary>
            DirectorySet = 0x0001,
        }

        private const uint FixedPartSize = 4; // u32 SessionId

        public class Builder(uint sessionId, string command)
        {
            public Builder Directory(string directory)
            {
                _directory = directory;
                return this;
            }

            public NowMsgExecRun Build()
            {
                return new NowMsgExecRun
                {
                    SessionId = sessionId,
                    Command = command,
                    Directory = _directory
                };
            }

            private string? _directory = null;
        }

        public uint SessionId { get; private init; }
        public string Command { get; private init; }
        public string? Directory { get; private init; }
    }
}