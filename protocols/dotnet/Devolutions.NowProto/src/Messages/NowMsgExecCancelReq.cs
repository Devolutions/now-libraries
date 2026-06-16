namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_EXEC_CANCEL_REQ_MSG message is used to cancel a remote execution session.
    ///
    /// NOW-PROTO: NOW_EXEC_CANCEL_REQ_MSG
    /// </summary>
    public class NowMsgExecCancelReq(uint sessionId) : INowSerialize, INowDeserialize<NowMsgExecCancelReq>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassExec;
        public static byte TypeMessageKind => 0x02; // NOW-PROTO: NOW_EXEC_CANCEL_REQ_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassExec;
        byte INowMessage.MessageKind => 0x02;

        // -- INowSerialize --

        ushort INowSerialize.Flags => 0;
        uint INowSerialize.BodySize => FixedPartSize;

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(SessionId);
        }

        // -- INowDeserialize --
        static NowMsgExecCancelReq INowDeserialize<NowMsgExecCancelReq>.Deserialize(ushort flags, NowReadCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var sessionId = cursor.ReadUInt32Le();

            return new NowMsgExecCancelReq(sessionId);
        }

        // -- impl --

        private const uint FixedPartSize = 4 /* u32 sessionId */;

        public uint SessionId { get; } = sessionId;
    }
}