namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_EXEC_STARTED_MSG message is sent by the server after the execution session has been
    /// successfully started.
    ///
    /// NOW-PROTO: NOW_EXEC_STARTED_MSG
    /// </summary>
    public class NowMsgExecStarted(uint sessionId) : INowSerialize, INowDeserialize<NowMsgExecStarted>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassExec;
        public static byte TypeMessageKind => 0x06; // NOW-PROTO: NOW_EXEC_STARTED_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassExec;
        byte INowMessage.MessageKind => 0x06;

        // -- INowSerialize --

        ushort INowSerialize.Flags => 0;
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
            var sessionId = cursor.ReadUInt32Le();

            return new NowMsgExecStarted(sessionId);
        }

        // -- impl --

        private const uint FixedPartSize = 4 /* u32 sessionId */;

        public uint SessionId { get; } = sessionId;
    }
}