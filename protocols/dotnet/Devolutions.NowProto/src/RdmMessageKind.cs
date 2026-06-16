namespace Devolutions.NowProto
{
    /// <summary>
    /// NOW-PROTO: NOW_RDM_*_MSG_ID message kinds
    /// </summary>
    public enum RdmMessageKind : byte
    {
        /// <summary>
        /// NOW-PROTO: NOW_RDM_CAPABILITIES_MSG_ID
        /// </summary>
        Capabilities = 0x01,

        /// <summary>
        /// NOW-PROTO: NOW_RDM_APP_START_MSG_ID
        /// </summary>
        AppStart = 0x02,

        /// <summary>
        /// NOW-PROTO: NOW_RDM_APP_ACTION_MSG_ID
        /// </summary>
        AppAction = 0x03,

        /// <summary>
        /// NOW-PROTO: NOW_RDM_APP_NOTIFY_MSG_ID
        /// </summary>
        AppNotify = 0x04,

        /// <summary>
        /// NOW-PROTO: NOW_RDM_SESSION_START_MSG_ID
        /// </summary>
        SessionStart = 0x05,

        /// <summary>
        /// NOW-PROTO: NOW_RDM_SESSION_ACTION_MSG_ID
        /// </summary>
        SessionAction = 0x06,

        /// <summary>
        /// NOW-PROTO: NOW_RDM_SESSION_NOTIFY_MSG_ID
        /// </summary>
        SessionNotify = 0x07,
    }
}