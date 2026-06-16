using Devolutions.NowProto.Exceptions;
using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_EXEC_RESULT_MSG message is used to return the result of an execution request.
    ///
    /// NOW_PROTO: NOW_EXEC_RESULT_MSG
    /// </summary>_EXEC_RESULT_MSG
    /// </summary>
    public class NowMsgExecResult : INowSerialize, INowDeserialize<NowMsgExecResult>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassExec;
        public static byte TypeMessageKind => 0x04; // NOW-PROTO: NOW_EXEC_RESULT_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassExec;
        byte INowMessage.MessageKind => 0x04;

        // -- INowSerialize --
        ushort INowSerialize.Flags => 0;
        uint INowSerialize.BodySize => FixedPartSize + _status.Size;

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(SessionId);
            cursor.WriteUint32Le(_exitCode);
            _status.Serialize(cursor);
        }

        // -- INowDeserialize --

        static NowMsgExecResult INowDeserialize<NowMsgExecResult>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var sessionId = cursor.ReadUInt32Le();
            var exitCode = cursor.ReadUInt32Le();
            var status = NowStatus.Deserialize(cursor);

            return new NowMsgExecResult(sessionId, exitCode, status);
        }

        // -- impl --

        private const uint FixedPartSize = 8; // u32 sessionId, u32 exitCode

        internal NowMsgExecResult(uint sessionId, uint exitCode, NowStatus status)
        {
            SessionId = sessionId;
            _exitCode = exitCode;
            _status = status;
        }

        public static NowMsgExecResult Success(uint sessionId, uint exitCode)
        {
            return new NowMsgExecResult(sessionId, exitCode, NowStatus.Success());
        }

        public static NowMsgExecResult Error(uint sessionId, NowStatusException exception)
        {
            return new NowMsgExecResult(
                sessionId,
                0,
                NowStatus.Error(exception)
            );
        }

        public bool IsSuccess => _status.IsSuccess;

        public uint GetExitCodeOrThrow()
        {
            _status.ThrowIfError();
            return _exitCode;
        }

        public uint SessionId { get; private init; }

        private readonly NowStatus _status;
        private readonly uint _exitCode;
    }
}