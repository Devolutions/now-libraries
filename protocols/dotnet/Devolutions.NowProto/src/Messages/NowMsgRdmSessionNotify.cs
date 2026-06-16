using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// Session notify values for NOW_RDM_SESSION_NOTIFY_MSG.
    /// NOW-PROTO: NOW_RDM_SESSION_NOTIFY_KIND
    /// </summary>
    public enum NowRdmSessionNotifyKind : uint
    {
        /// <summary>
        /// The session has been closed.
        /// </summary>
        Close = 0x00000001,

        /// <summary>
        /// The session has been focused.
        /// </summary>
        Focus = 0x00000002,
    }

    /// <summary>
    /// The NOW_RDM_SESSION_NOTIFY_MSG is used by the server to notify of a session state change,
    /// such as a session closing, or a session focus change.
    ///
    /// NOW-PROTO: NOW_RDM_SESSION_NOTIFY_MSG
    /// </summary>
    public class NowMsgRdmSessionNotify : INowSerialize, INowDeserialize<NowMsgRdmSessionNotify>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassRdm;
        public static byte TypeMessageKind => (byte)RdmMessageKind.SessionNotify;

        byte INowMessage.MessageClass => NowMessage.ClassRdm;
        byte INowMessage.MessageKind => (byte)RdmMessageKind.SessionNotify;

        // -- INowDeserialize --

        static NowMsgRdmSessionNotify INowDeserialize<NowMsgRdmSessionNotify>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);

            var sessionNotifyKind = (NowRdmSessionNotifyKind)cursor.ReadUInt32Le();
            var sessionId = cursor.ReadGuid();
            var logData = cursor.ReadVarStr();

            return new NowMsgRdmSessionNotify
            {
                SessionNotifyKind = sessionNotifyKind,
                SessionId = sessionId,
                LogData = logData,
            };
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags => 0;

        uint INowSerialize.BodySize => FixedPartSize
                                     + NowGuid.LengthOf(SessionId)
                                     + NowVarStr.LengthOf(LogData);

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le((uint)SessionNotifyKind);
            cursor.WriteGuid(SessionId);
            cursor.WriteVarStr(LogData);
        }

        // -- impl --

        private const uint FixedPartSize = 4; // 4 bytes for session_notify field

        /// <summary>
        /// Hide public default constructor.
        /// </summary>
        internal NowMsgRdmSessionNotify() { }

        public NowMsgRdmSessionNotify(NowRdmSessionNotifyKind sessionNotifyKind, Guid sessionId, string logData = "")
        {
            SessionNotifyKind = sessionNotifyKind;
            SessionId = sessionId;
            LogData = logData ?? "";
        }

        /// <summary>
        /// The session notify id.
        /// </summary>
        public NowRdmSessionNotifyKind SessionNotifyKind { get; private init; } = NowRdmSessionNotifyKind.Close;

        /// <summary>
        /// Session id, encoded as a NOW_GUID structure. Can be null in some cases, such as when no session is in focus.
        /// </summary>
        public Guid SessionId { get; private init; } = Guid.Empty;

        /// <summary>
        /// The serialized RDM XML log information object, encoded in a NOW_VARSTR structure.
        /// Primarily used to send logs back to RDM on session close. This field should be empty for session notifications that aren't logged, such as focus changes.
        /// </summary>
        public string LogData { get; private init; } = "";

        /// <summary>
        /// Create a notification that a session has been closed.
        /// </summary>
        public static NowMsgRdmSessionNotify Close(Guid sessionId, string logData = "")
        {
            return new NowMsgRdmSessionNotify(NowRdmSessionNotifyKind.Close, sessionId, logData);
        }

        /// <summary>
        /// Create a notification that a session has been focused.
        /// </summary>
        public static NowMsgRdmSessionNotify Focus(Guid sessionId, string logData = "")
        {
            return new NowMsgRdmSessionNotify(NowRdmSessionNotifyKind.Focus, sessionId, logData);
        }
    }
}