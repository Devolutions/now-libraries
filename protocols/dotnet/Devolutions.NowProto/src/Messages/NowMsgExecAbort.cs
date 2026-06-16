namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_EXEC_ABORT_MSG message is used to abort a remote execution immediately due to an
    /// unrecoverable error. This message can be sent at any time without an explicit response message.
    /// The session is considered aborted as soon as this message is sent.
    ///
    /// NOW-PROTO: NOW_EXEC_ABORT_MSG
    /// </summary>
    public class NowMsgExecAbort(uint sessionId, uint exitCode) : INowSerialize, INowDeserialize<NowMsgExecAbort>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassExec;
        public static byte TypeMessageKind => 0x01; // NOW-PROTO: NOW_EXEC_ABORT_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassExec;
        byte INowMessage.MessageKind => 0x01;

        // -- INowSerialize --

        public ushort Flags => 0;
        public uint BodySize => FixedPartSize;

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(SessionId);
            cursor.WriteUint32Le(ExitCode);
        }

        // -- INowDeserialize --
        static NowMsgExecAbort INowDeserialize<NowMsgExecAbort>.Deserialize(ushort flags, NowReadCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var sessionId = cursor.ReadUInt32Le();
            var exitCode = cursor.ReadUInt32Le();

            return new NowMsgExecAbort(sessionId, exitCode);
        }

        // -- impl --

        private const uint FixedPartSize = 8 /* u32 sessionId + u32 exitCode */;

        public uint SessionId { get; } = sessionId;
        public uint ExitCode { get; } = exitCode;
    }
}