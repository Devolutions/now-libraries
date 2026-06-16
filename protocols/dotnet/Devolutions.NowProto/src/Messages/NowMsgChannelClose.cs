using Devolutions.NowProto.Exceptions;
using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// Channel close notice, could be sent by either parties at any moment of communication to
    /// gracefully close DVC channel.
    ///
    /// NOW-PROTO: NOW_CHANNEL_CLOSE_MSG
    /// </summary>
    public class NowMsgChannelClose : INowSerialize, INowDeserialize<NowMsgChannelClose>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassChannel;
        public static byte TypeMessageKind => 0x03; // NOW-PROTO: NOW_CHANNEL_CLOSE_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassChannel;
        byte INowMessage.MessageKind => 0x03;

        // -- INowDeserialize --

        static NowMsgChannelClose INowDeserialize<NowMsgChannelClose>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            var status = NowStatus.Deserialize(cursor);

            return new NowMsgChannelClose(status);
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags => 0;
        uint INowSerialize.BodySize => _status.Size;

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            _status.Serialize(cursor);
        }

        // -- impl ---

        internal NowMsgChannelClose(NowStatus status)
        {
            _status = status;
        }

        /// <summary>
        /// Create a new NOW_CHANNEL_CLOSE_MSG message with a success status.
        /// </summary>
        public static NowMsgChannelClose Graceful()
        {
            return new NowMsgChannelClose(NowStatus.Success());
        }

        /// <summary>
        /// Create a new NOW_CHANNEL_CLOSE_MSG message with error status.
        /// </summary>
        public static NowMsgChannelClose Error(NowStatusException exception)
        {
            return new NowMsgChannelClose(NowStatus.Error(exception));
        }

        public bool IsGraceful => _status.IsSuccess;

        public void ThrowIfError()
        {
            _status.ThrowIfError();
        }

        private readonly NowStatus _status;
    }
}