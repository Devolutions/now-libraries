namespace Devolutions.NowProto.Capabilities
{
    /// <summary>
    /// NOW-PROTO: NOW_SESSION_CAPSET_MSG sessionCapset field.
    /// </summary>
    [Flags]
    public enum NowCapabilitySession : ushort
    {
        /// <summary>
        /// Empty capability set.
        /// </summary>
        None = 0x0000,
        /// <summary>
        /// Session lock command support.
        ///
        /// NOW-PROTO: NOW_CAP_SESSION_LOCK
        /// </summary>
        Lock = 0x0001,
        /// <summary>
        /// Session logoff command support.
        ///
        /// NOW-PROTO: NOW_CAP_SESSION_LOGOFF
        /// </summary>
        Logoff = 0x0002,
        /// <summary>
        /// Message box command support.
        ///
        /// NOW-PROTO: NOW_CAP_SESSION_MSGBOX
        /// </summary>
        Msgbox = 0x0004,
        /// <summary>
        /// Set keyboard layout command support.
        ///
        /// NOW-PROTO: NOW_CAP_SESSION_SET_KBD_LAYOUT
        /// </summary>
        SetKbdLayout = 0x0008,
        /// <summary>
        /// Window recording command support.
        ///
        /// NOW-PROTO: NOW_CAP_SESSION_WINDOW_RECORDING
        /// </summary>
        WindowRecording = 0x0010,

        All = Lock | Logoff | Msgbox | SetKbdLayout | WindowRecording,
    }
}