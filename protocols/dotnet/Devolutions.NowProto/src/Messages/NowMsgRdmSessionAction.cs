using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// Session action types for RDM.
    /// NOW-PROTO: NOW_RDM_SESSION_ACTION
    /// </summary>
    public enum NowRdmSessionAction : uint
    {
        /// <summary>
        /// Close or terminate the session.
        /// </summary>
        Close = 0x00000001,

        /// <summary>
        /// Focus the embedded tab of a specific session.
        /// </summary>
        Focus = 0x00000002,
    }

    /// <summary>
    /// The NOW_RDM_SESSION_ACTION_MSG is used by the client to trigger an action on an existing session,
    /// such as closing or focusing a session.
    ///
    /// NOW-PROTO: NOW_RDM_SESSION_ACTION_MSG
    /// </summary>
    public class NowMsgRdmSessionAction : INowSerialize, INowDeserialize<NowMsgRdmSessionAction>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassRdm;
        public static byte TypeMessageKind => (byte)RdmMessageKind.SessionAction;

        byte INowMessage.MessageClass => NowMessage.ClassRdm;
        byte INowMessage.MessageKind => (byte)RdmMessageKind.SessionAction;

        // -- INowDeserialize --

        static NowMsgRdmSessionAction INowDeserialize<NowMsgRdmSessionAction>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);

            var sessionAction = (NowRdmSessionAction)cursor.ReadUInt32Le();
            var sessionId = cursor.ReadGuid();

            return new NowMsgRdmSessionAction
            {
                SessionAction = sessionAction,
                SessionId = sessionId,
            };
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags => 0;

        uint INowSerialize.BodySize => FixedPartSize + NowGuid.LengthOf(SessionId);

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le((uint)SessionAction);
            cursor.WriteGuid(SessionId);
        }

        // -- impl --

        private const uint FixedPartSize = 4; // 4 bytes for session_action field

        /// <summary>
        /// Hide public default constructor.
        /// </summary>
        internal NowMsgRdmSessionAction() { }

        public NowMsgRdmSessionAction(NowRdmSessionAction sessionAction, Guid sessionId)
        {
            SessionAction = sessionAction;
            SessionId = sessionId;
        }

        /// <summary>
        /// The session action id.
        /// </summary>
        public NowRdmSessionAction SessionAction { get; private init; } = NowRdmSessionAction.Close;

        /// <summary>
        /// Session id, encoded as a NOW_GUID structure.
        /// </summary>
        public Guid SessionId { get; private init; } = Guid.Empty;

        /// <summary>
        /// Create a message to close or terminate the session.
        /// </summary>
        public static NowMsgRdmSessionAction Close(Guid sessionId)
        {
            return new NowMsgRdmSessionAction(NowRdmSessionAction.Close, sessionId);
        }

        /// <summary>
        /// Create a message to focus the embedded tab of a specific session.
        /// </summary>
        public static NowMsgRdmSessionAction Focus(Guid sessionId)
        {
            return new NowMsgRdmSessionAction(NowRdmSessionAction.Focus, sessionId);
        }
    }
}