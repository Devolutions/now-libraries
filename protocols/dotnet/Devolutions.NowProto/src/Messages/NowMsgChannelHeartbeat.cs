namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// Periodic heartbeat message sent by the server. If the client does not
    /// receive this message within the specified interval, it should consider
    /// the connection as lost.
    ///
    /// NOW-PROTO: NOW_CHANNEL_HEARTBEAT_MSG
    /// </summary>
    public class NowMsgChannelHeartbeat : INowSerialize, INowDeserialize<NowMsgChannelHeartbeat>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassChannel;
        public static byte TypeMessageKind => 0x02; // NOW-PROTO: NOW_CHANNEL_HEARTBEAT_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassChannel;
        byte INowMessage.MessageKind => 0x02;

        // -- INowDeserialize --

        static NowMsgChannelHeartbeat INowDeserialize<NowMsgChannelHeartbeat>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            return new NowMsgChannelHeartbeat();
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags { get; } = 0;
        uint INowSerialize.BodySize => 0;

        void INowSerialize.SerializeBody(NowWriteCursor cursor) { }
    }
}