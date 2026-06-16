using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_RDM_SESSION_START_MSG message is used to start a new RDM session.
    ///
    /// NOW-PROTO: NOW_RDM_SESSION_START_MSG
    /// </summary>
    public class NowMsgRdmSessionStart : INowSerialize, INowDeserialize<NowMsgRdmSessionStart>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassRdm;
        public static byte TypeMessageKind => (byte)RdmMessageKind.SessionStart;

        byte INowMessage.MessageClass => NowMessage.ClassRdm;
        byte INowMessage.MessageKind => (byte)RdmMessageKind.SessionStart;

        // -- INowDeserialize --

        static NowMsgRdmSessionStart INowDeserialize<NowMsgRdmSessionStart>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            var sessionId = cursor.ReadGuid();
            var connectionId = cursor.ReadGuid();
            var connectionData = cursor.ReadVarStr();

            return new NowMsgRdmSessionStart
            {
                SessionId = sessionId,
                ConnectionId = connectionId,
                ConnectionData = connectionData,
            };
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags => 0;

        uint INowSerialize.BodySize => NowGuid.LengthOf(SessionId)
                                     + NowGuid.LengthOf(ConnectionId)
                                     + NowVarStr.LengthOf(ConnectionData);

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.WriteGuid(SessionId);
            cursor.WriteGuid(ConnectionId);
            cursor.WriteVarStr(ConnectionData);
        }

        // -- impl --

        /// <summary>
        /// Hide public default constructor.
        /// </summary>
        internal NowMsgRdmSessionStart() { }

        /// <summary>
        /// Session id, encoded as a NOW_GUID structure.
        /// </summary>
        public Guid SessionId { get; private init; } = Guid.Empty;

        /// <summary>
        /// Connection id, encoded as a NOW_GUID structure. Reserved for future use. set to null when unused.
        /// </summary>
        public Guid ConnectionId { get; private init; } = Guid.Empty;

        /// <summary>
        /// The serialized RDM XML connection object, encoded in a NOW_VARSTR structure.
        /// </summary>
        public string ConnectionData { get; private init; } = "";

        /// <summary>
        /// Create a new RDM session start message.
        /// </summary>
        /// <param name="sessionId">Unique session identifier.</param>
        /// <param name="connectionId">Unique connection identifier.</param>
        /// <param name="connectionData">Connection-related data.</param>
        public NowMsgRdmSessionStart(Guid sessionId, Guid connectionId, string connectionData)
        {
            SessionId = sessionId;
            ConnectionId = connectionId;
            ConnectionData = connectionData ?? "";
        }
    }
}