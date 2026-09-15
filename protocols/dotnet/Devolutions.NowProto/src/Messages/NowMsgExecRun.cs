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
            (!string.IsNullOrEmpty(Directory) ? MsgFlags.DirectorySet : 0) |
            (Elevated ? MsgFlags.Elevated : 0)
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
                Directory = directory,
                Elevated = ((MsgFlags)flags).HasFlag(MsgFlags.Elevated)
            };
        }

        // -- impl --

        public NowMsgExecRun(uint sessionId, string command)
        {
            SessionId = sessionId;
            Command = command;
            Directory = null;
            Elevated = false;
        }

        private NowMsgExecRun()
        {
            SessionId = 0;
            Command = string.Empty;
            Directory = null;
            Elevated = false;
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

            /// <summary>
            /// Execute the command with elevated privileges. The elevation mechanism is chosen by
            /// the host and advertised in execCapset.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_RUN_ELEVATED
            /// </summary>
            Elevated = 0x0002,
        }

        private const uint FixedPartSize = 4; // u32 SessionId

        public class Builder(uint sessionId, string command)
        {
            public Builder Directory(string directory)
            {
                _directory = directory;
                return this;
            }

            public Builder EnableElevated()
            {
                _elevated = true;
                return this;
            }

            public NowMsgExecRun Build()
            {
                return new NowMsgExecRun
                {
                    SessionId = sessionId,
                    Command = command,
                    Directory = _directory,
                    Elevated = _elevated
                };
            }

            private string? _directory = null;
            private bool _elevated = false;
        }

        public uint SessionId { get; private init; }
        public string Command { get; private init; }
        public string? Directory { get; private init; }
        public bool Elevated { get; private init; }
    }
}