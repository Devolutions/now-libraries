using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// Application action types for RDM.
    /// NOW-PROTO: NOW_RDM_APP_ACTION
    /// </summary>
    public enum NowRdmAppAction : uint
    {
        /// <summary>
        /// Close (terminate) RDM application.
        /// </summary>
        Close = 0x00000001,

        /// <summary>
        /// Minimize RDM application window.
        /// </summary>
        Minimize = 0x00000002,

        /// <summary>
        /// Maximize RDM application window.
        /// </summary>
        Maximize = 0x00000003,

        /// <summary>
        /// Restore RDM application window.
        /// </summary>
        Restore = 0x00000004,

        /// <summary>
        /// Toggle RDM fullscreen mode.
        /// </summary>
        Fullscreen = 0x00000005,
    }

    /// <summary>
    /// The NOW_RDM_APP_ACTION_MSG is sent by the client to trigger an application state change.
    ///
    /// NOW-PROTO: NOW_RDM_APP_ACTION_MSG
    /// </summary>
    public class NowMsgRdmAppAction : INowSerialize, INowDeserialize<NowMsgRdmAppAction>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassRdm;
        public static byte TypeMessageKind => (byte)RdmMessageKind.AppAction;

        byte INowMessage.MessageClass => NowMessage.ClassRdm;
        byte INowMessage.MessageKind => (byte)RdmMessageKind.AppAction;

        // -- INowDeserialize --

        static NowMsgRdmAppAction INowDeserialize<NowMsgRdmAppAction>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);

            var appAction = (NowRdmAppAction)cursor.ReadUInt32Le();
            var actionData = cursor.ReadVarStr();

            return new NowMsgRdmAppAction
            {
                AppAction = appAction,
                ActionData = actionData,
            };
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags => 0;

        uint INowSerialize.BodySize => FixedPartSize + NowVarStr.LengthOf(ActionData);

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le((uint)AppAction);
            cursor.WriteVarStr(ActionData);
        }

        // -- impl --

        private const uint FixedPartSize = 4; // 4 bytes

        /// <summary>
        /// Hide public default constructor.
        /// </summary>
        internal NowMsgRdmAppAction() { }

        public NowMsgRdmAppAction(NowRdmAppAction appAction, string actionData = "")
        {
            AppAction = appAction;
            ActionData = actionData ?? "";
        }

        /// <summary>
        /// The application action.
        /// </summary>
        public NowRdmAppAction AppAction { get; private init; } = NowRdmAppAction.Close;

        /// <summary>
        /// A serialized XML object, encoded in a NOW_VARSTR structure.
        /// This field is reserved for future use and should be left empty.
        /// </summary>
        public string ActionData { get; private init; } = "";

        /// <summary>
        /// Create a message to close (terminate) the RDM application.
        /// </summary>
        public static NowMsgRdmAppAction Close(string actionData = "")
        {
            return new NowMsgRdmAppAction(NowRdmAppAction.Close, actionData);
        }

        /// <summary>
        /// Create a message to minimize the RDM application window.
        /// </summary>
        public static NowMsgRdmAppAction Minimize(string actionData = "")
        {
            return new NowMsgRdmAppAction(NowRdmAppAction.Minimize, actionData);
        }

        /// <summary>
        /// Create a message to maximize the RDM application window.
        /// </summary>
        public static NowMsgRdmAppAction Maximize(string actionData = "")
        {
            return new NowMsgRdmAppAction(NowRdmAppAction.Maximize, actionData);
        }

        /// <summary>
        /// Create a message to restore the RDM application window.
        /// </summary>
        public static NowMsgRdmAppAction Restore(string actionData = "")
        {
            return new NowMsgRdmAppAction(NowRdmAppAction.Restore, actionData);
        }

        /// <summary>
        /// Create a message to toggle RDM fullscreen mode.
        /// </summary>
        public static NowMsgRdmAppAction Fullscreen(string actionData = "")
        {
            return new NowMsgRdmAppAction(NowRdmAppAction.Fullscreen, actionData);
        }
    }
}