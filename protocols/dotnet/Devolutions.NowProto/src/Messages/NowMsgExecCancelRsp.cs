using Devolutions.NowProto.Exceptions;
using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_EXEC_RESULT_MSG message is used to return the result of an execution request.
    ///
    /// NOW_PROTO: NOW_EXEC_RESULT_MSG
    /// </summary>
    public class NowMsgExecCancelRsp : INowSerialize, INowDeserialize<NowMsgExecCancelRsp>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassExec;
        public static byte TypeMessageKind => 0x03; // NOW-PROTO: NOW_EXEC_RESULT_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassExec;
        byte INowMessage.MessageKind => 0x03;

        // -- INowSerialize --
        ushort INowSerialize.Flags => 0;
        uint INowSerialize.BodySize => FixedPartSize + _status.Size;

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(SessionId);
            _status.Serialize(cursor);
        }

        // -- INowDeserialize --

        static NowMsgExecCancelRsp INowDeserialize<NowMsgExecCancelRsp>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var sessionId = cursor.ReadUInt32Le();
            var status = NowStatus.Deserialize(cursor);

            return new NowMsgExecCancelRsp(sessionId, status);
        }

        // -- impl --

        private const uint FixedPartSize = 4; // u32 sessionId

        internal NowMsgExecCancelRsp(uint sessionId, NowStatus status)
        {
            SessionId = sessionId;
            _status = status;
        }

        public static NowMsgExecCancelRsp Success(uint sessionId)
        {
            return new NowMsgExecCancelRsp(sessionId, NowStatus.Success());
        }

        public static NowMsgExecCancelRsp Error(uint sessionId, NowStatusException exception)
        {
            return new NowMsgExecCancelRsp(
                sessionId,
                NowStatus.Error(exception)
            );
        }

        public bool IsSuccess => _status.IsSuccess;

        public void ThrowIfError()
        {
            _status.ThrowIfError();
        }

        public uint SessionId { get; private init; }

        private readonly NowStatus _status;
    }
}