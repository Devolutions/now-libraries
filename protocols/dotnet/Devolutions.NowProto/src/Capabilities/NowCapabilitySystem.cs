namespace Devolutions.NowProto.Capabilities
{
    /// <summary>
    /// NOW-PROTO: NOW_CHANNEL_CAPSET_MSG systemCapset field.
    /// </summary>
    [Flags]
    public enum NowCapabilitySystem : ushort
    {
        /// <summary>
        /// Empty capability set.
        /// </summary>
        None = 0x0000,
        /// <summary>
        /// System shutdown command support.
        ///
        /// NOW-PROTO: NOW_CAP_SYSTEM_SHUTDOWN
        /// </summary>
        Shutdown = 0x0001,

        All = Shutdown,
    }
}