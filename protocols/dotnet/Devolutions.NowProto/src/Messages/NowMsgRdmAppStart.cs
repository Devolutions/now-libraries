namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// NOW-PROTO: NOW_RDM_LAUNCH_FLAGS
    /// </summary>
    [Flags]
    public enum NowRdmLaunchFlags : uint
    {
        /// <summary>
        /// No launch flags.
        /// </summary>
        None = 0x00000000,

        /// <summary>
        /// Launch RDM in Jump mode.
        /// NOW-PROTO: NOW_RDM_LAUNCH_FLAG_JUMP_MODE
        /// </summary>
        JumpMode = 0x00000001,

        /// <summary>
        /// Launch RDM maximized.
        /// NOW-PROTO: NOW_RDM_LAUNCH_FLAG_MAXIMIZED
        /// </summary>
        Maximized = 0x00000002,

        /// <summary>
        /// Launch RDM in fullscreen mode.
        /// NOW-PROTO: NOW_RDM_LAUNCH_FLAG_FULLSCREEN
        /// </summary>
        Fullscreen = 0x00000004,
    }

    /// <summary>
    /// The NOW_RDM_APP_START_MSG message is used to launch RDM.
    ///
    /// NOW-PROTO: NOW_RDM_APP_START_MSG
    /// </summary>
    public class NowMsgRdmAppStart : INowSerialize, INowDeserialize<NowMsgRdmAppStart>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassRdm;
        public static byte TypeMessageKind => (byte)RdmMessageKind.AppStart;

        byte INowMessage.MessageClass => NowMessage.ClassRdm;
        byte INowMessage.MessageKind => (byte)RdmMessageKind.AppStart;

        // -- INowDeserialize --

        static NowMsgRdmAppStart INowDeserialize<NowMsgRdmAppStart>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);

            var launchFlags = (NowRdmLaunchFlags)cursor.ReadUInt32Le();
            var timeout = cursor.ReadUInt32Le();

            return new NowMsgRdmAppStart
            {
                LaunchFlags = launchFlags,
                Timeout = timeout,
            };
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags => 0;
        uint INowSerialize.BodySize => FixedPartSize;

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le((uint)LaunchFlags);
            cursor.WriteUint32Le(Timeout);
        }

        // -- impl --

        private const uint FixedPartSize = 8; // 4 + 4 bytes

        /// <summary>
        /// Hide public default constructor.
        /// </summary>
        internal NowMsgRdmAppStart() { }

        /// <summary>
        /// The application launch flags.
        /// </summary>
        public NowRdmLaunchFlags LaunchFlags { get; private init; } = NowRdmLaunchFlags.None;

        /// <summary>
        /// The launch timeout, in seconds, that the client is willing to wait for RDM to launch and become ready.
        /// A recommended default value is 45 seconds.
        /// </summary>
        public uint Timeout { get; private init; } = 0;

        /// <summary>
        /// Check if RDM should be launched in Jump mode.
        /// </summary>
        public bool IsJumpMode => LaunchFlags.HasFlag(NowRdmLaunchFlags.JumpMode);

        /// <summary>
        /// Check if RDM should be launched maximized.
        /// </summary>
        public bool IsMaximized => LaunchFlags.HasFlag(NowRdmLaunchFlags.Maximized);

        /// <summary>
        /// Check if RDM should be launched in fullscreen mode.
        /// </summary>
        public bool IsFullscreen => LaunchFlags.HasFlag(NowRdmLaunchFlags.Fullscreen);

        /// <summary>
        /// Builder for NowMsgRdmAppStart with optional configuration.
        /// </summary>
        public class Builder()
        {
            public Builder Timeout(uint timeout)
            {
                _timeout = timeout;
                return this;
            }

            public Builder WithJumpMode()
            {
                _launchFlags |= NowRdmLaunchFlags.JumpMode;
                return this;
            }

            public Builder WithMaximized()
            {
                _launchFlags |= NowRdmLaunchFlags.Maximized;
                return this;
            }

            public Builder WithFullscreen()
            {
                _launchFlags |= NowRdmLaunchFlags.Fullscreen;
                return this;
            }

            public NowMsgRdmAppStart Build()
            {
                return new NowMsgRdmAppStart
                {
                    LaunchFlags = _launchFlags,
                    Timeout = _timeout,
                };
            }

            private NowRdmLaunchFlags _launchFlags = NowRdmLaunchFlags.None;
            private uint _timeout = 45; // Default timeout is 45 seconds
        }
    }
}