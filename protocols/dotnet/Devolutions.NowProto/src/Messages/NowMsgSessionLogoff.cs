namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_SESSION_LOGOFF_MSG is used to request a user session logoff.
    ///
    /// NOW_PROTO: NOW_SESSION_LOGOFF_MSG
    /// </summary>
    public class NowMsgSessionLogoff : INowSerialize, INowDeserialize<NowMsgSessionLogoff>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassSession;
        public static byte TypeMessageKind => 0x02; // NOW-PROTO: NOW_SESSION_LOGOFF_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassSession;
        byte INowMessage.MessageKind => 0x02;

        // -- INowDeserialize --

        static NowMsgSessionLogoff INowDeserialize<NowMsgSessionLogoff>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            return new NowMsgSessionLogoff();
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags { get; } = 0;
        uint INowSerialize.BodySize => 0;

        void INowSerialize.SerializeBody(NowWriteCursor cursor) { }
    }
}