using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// Application state values for NOW_RDM_APP_NOTIFY_MSG.
    /// NOW-PROTO: NOW_RDM_APP_STATE
    /// </summary>
    public enum NowRdmAppState : uint
    {
        /// <summary>
        /// RDM is launched and ready to launch connections.
        /// </summary>
        Ready = 0x00000001,

        /// <summary>
        /// RDM has failed to launch.
        /// </summary>
        Failed = 0x00000002,

        /// <summary>
        /// RDM has been closed or terminated.
        /// </summary>
        Closed = 0x00000003,

        /// <summary>
        /// RDM has been minimized.
        /// </summary>
        Minimized = 0x00000004,

        /// <summary>
        /// RDM has been maximized.
        /// </summary>
        Maximized = 0x00000005,

        /// <summary>
        /// RDM has been restored.
        /// </summary>
        Restored = 0x00000006,

        /// <summary>
        /// RDM fullscreen mode has been toggled.
        /// </summary>
        Fullscreen = 0x00000007,
    }

    /// <summary>
    /// Reason code values for NOW_RDM_APP_NOTIFY_MSG.
    /// NOW-PROTO: NOW_RDM_REASON
    /// </summary>
    public enum NowRdmReason : uint
    {
        /// <summary>
        /// Unspecified reason (default value).
        /// </summary>
        NotSpecified = 0x00000000,

        /// <summary>
        /// The application state change was user-initiated.
        /// </summary>
        UserInitiated = 0x00000001,

        /// <summary>
        /// RDM has failed to launch because it is not installed.
        /// </summary>
        NotInstalled = 0x00000002,

        /// <summary>
        /// RDM is installed, but something prevented it from starting up properly.
        /// </summary>
        StartupFailure = 0x00000003,

        /// <summary>
        /// RDM is installed and could be launched but it wasn't ready before the expected timeout.
        /// </summary>
        LaunchTimeout = 0x00000004,
    }

    /// <summary>
    /// The NOW_RDM_APP_NOTIFY_MSG is sent by the server to notify the client of an RDM app state change, such as readiness.
    ///
    /// NOW-PROTO: NOW_RDM_APP_NOTIFY_MSG
    /// </summary>
    public class NowMsgRdmAppNotify : INowSerialize, INowDeserialize<NowMsgRdmAppNotify>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassRdm;
        public static byte TypeMessageKind => (byte)RdmMessageKind.AppNotify;

        byte INowMessage.MessageClass => NowMessage.ClassRdm;
        byte INowMessage.MessageKind => (byte)RdmMessageKind.AppNotify;

        // -- INowDeserialize --

        static NowMsgRdmAppNotify INowDeserialize<NowMsgRdmAppNotify>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);

            var appState = (NowRdmAppState)cursor.ReadUInt32Le();
            var reasonCode = (NowRdmReason)cursor.ReadUInt32Le();
            var notifyData = cursor.ReadVarStr();

            return new NowMsgRdmAppNotify
            {
                AppState = appState,
                ReasonCode = reasonCode,
                NotifyData = notifyData,
            };
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags => 0;

        uint INowSerialize.BodySize => FixedPartSize + NowVarStr.LengthOf(NotifyData);

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le((uint)AppState);
            cursor.WriteUint32Le((uint)ReasonCode);
            cursor.WriteVarStr(NotifyData);
        }

        // -- impl --

        private const uint FixedPartSize = 8; // 4 + 4 bytes

        /// <summary>
        /// Hide public default constructor.
        /// </summary>
        internal NowMsgRdmAppNotify() { }

        public NowMsgRdmAppNotify(NowRdmAppState appState, NowRdmReason reasonCode = NowRdmReason.NotSpecified, string notifyData = "")
        {
            AppState = appState;
            ReasonCode = reasonCode;
            NotifyData = notifyData ?? "";
        }

        /// <summary>
        /// The application state.
        /// </summary>
        public NowRdmAppState AppState { get; private init; } = NowRdmAppState.Ready;

        /// <summary>
        /// A reason code specific to the application state change.
        /// </summary>
        public NowRdmReason ReasonCode { get; private init; } = NowRdmReason.NotSpecified;

        /// <summary>
        /// A serialized XML object, encoded in a NOW_VARSTR structure.
        /// This field is reserved for future use and should be left empty.
        /// </summary>
        public string NotifyData { get; private init; } = "";
    }
}