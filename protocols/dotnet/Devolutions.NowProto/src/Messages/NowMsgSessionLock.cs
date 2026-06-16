namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_SESSION_LOCK_MSG is used to request locking the user session.
    ///
    /// NOW_PROTO: NOW_SESSION_LOCK_MSG
    /// </summary>
    public class NowMsgSessionLock : INowSerialize, INowDeserialize<NowMsgSessionLock>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassSession;
        public static byte TypeMessageKind => 0x01; // NOW-PROTO: NOW_SESSION_LOCK_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassSession;
        byte INowMessage.MessageKind => 0x01;

        // -- INowDeserialize --

        static NowMsgSessionLock INowDeserialize<NowMsgSessionLock>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            return new NowMsgSessionLock();
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags { get; } = 0;
        uint INowSerialize.BodySize => 0;

        void INowSerialize.SerializeBody(NowWriteCursor cursor) { }
    }
}