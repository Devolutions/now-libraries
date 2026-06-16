namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_SESSION_WINDOW_REC_STOP_MSG message is used to stop window recording.
    ///
    /// NOW_PROTO: NOW_SESSION_WINDOW_REC_STOP_MSG
    /// </summary>
    public class NowMsgSessionWindowRecStop : INowSerialize, INowDeserialize<NowMsgSessionWindowRecStop>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassSession;
        public static byte TypeMessageKind => (byte)SessionMessageKind.WindowRecStop;

        byte INowMessage.MessageClass => NowMessage.ClassSession;
        byte INowMessage.MessageKind => (byte)SessionMessageKind.WindowRecStop;

        // -- INowSerialize --

        ushort INowSerialize.Flags => 0;

        uint INowSerialize.BodySize => 0;

        public void SerializeBody(NowWriteCursor cursor)
        {
            // No body
        }

        // -- INowDeserialize --
        public static NowMsgSessionWindowRecStop Deserialize(ushort flags, NowReadCursor cursor)
        {
            return new NowMsgSessionWindowRecStop();
        }

        // -- impl --

        public NowMsgSessionWindowRecStop()
        {
        }
    }
}