using System;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_EXEC_STARTED_MSG message is sent by the server after the execution session has been
    /// successfully started.
    ///
    /// NOW-PROTO: NOW_EXEC_STARTED_MSG
    /// </summary>
    public class NowMsgExecStarted(uint sessionId, bool noIoRedirection = false) : INowSerialize, INowDeserialize<NowMsgExecStarted>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassExec;
        public static byte TypeMessageKind => 0x06; // NOW-PROTO: NOW_EXEC_STARTED_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassExec;
        byte INowMessage.MessageKind => 0x06;

        // -- INowSerialize --

        ushort INowSerialize.Flags => (ushort)(NoIoRedirection ? MsgFlags.NoIoRedirection : MsgFlags.None);
        uint INowSerialize.BodySize => FixedPartSize;

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(SessionId);
        }

        // -- INowDeserialize --
        static NowMsgExecStarted INowDeserialize<NowMsgExecStarted>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var msgFlags = (MsgFlags)flags;
            var sessionId = cursor.ReadUInt32Le();

            return new NowMsgExecStarted(sessionId, msgFlags.HasFlag(MsgFlags.NoIoRedirection));
        }

        // -- impl --

        [Flags]
        private enum MsgFlags : ushort
        {
            None = 0x0000,

            /// <summary>
            /// Set when NOW_EXEC_FLAG_*_IO_REDIRECTION was requested, but the server could not
            /// redirect standard streams for this session.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_STARTED_NO_IO_REDIRECTION
            /// </summary>
            NoIoRedirection = 0x0001,
        }

        private const uint FixedPartSize = 4 /* u32 sessionId */;

        public uint SessionId { get; } = sessionId;
        public bool NoIoRedirection { get; } = noIoRedirection;
    }
}