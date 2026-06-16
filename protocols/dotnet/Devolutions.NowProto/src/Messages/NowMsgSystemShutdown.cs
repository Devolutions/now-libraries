using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_SYSTEM_SHUTDOWN_MSG structure is used to request a system shutdown.
    ///
    /// NOW_PROTO: NOW_SYSTEM_SHUTDOWN_MSG
    /// </summary>
    public class NowMsgSystemShutdown : INowSerialize, INowDeserialize<NowMsgSystemShutdown>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassSystem;
        public static byte TypeMessageKind => 0x03; // NOW-PROTO: NOW_SYSTEM_SHUTDOWN_ID

        byte INowMessage.MessageClass => NowMessage.ClassSystem;
        byte INowMessage.MessageKind => 0x03;

        // -- INowSerialize --

        ushort INowSerialize.Flags => (ushort)(
            (Force ? MsgFlags.Force : MsgFlags.None)
            | (Reboot ? MsgFlags.Reboot : MsgFlags.None)
        );
        uint INowSerialize.BodySize => FixedPartSize + NowVarStr.LengthOf(Message);

        public void SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le((uint)Timeout.TotalSeconds);
            cursor.WriteVarStr(Message);
        }

        // -- INowDeserialize --

        static NowMsgSystemShutdown INowDeserialize<NowMsgSystemShutdown>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var headerFlags = (MsgFlags)flags;
            var timeout = cursor.ReadUInt32Le();
            var message = cursor.ReadVarStr();

            return new NowMsgSystemShutdown()
            {
                Force = headerFlags.HasFlag(MsgFlags.Force),
                Reboot = headerFlags.HasFlag(MsgFlags.Reboot),
                _timeout = timeout,
                Message = message,
            };
        }

        // -- impl --

        private const uint FixedPartSize = 4 /* u32 timeout */;

        [Flags]
        private enum MsgFlags : ushort
        {
            None = 0x0000,
            /// NOW-PROTO: NOW_SHUTDOWN_FLAG_FORCE
            Force = 0x0001,
            /// NOW-PROTO: NOW_SHUTDOWN_FLAG_REBOOT
            Reboot = 0x0002,
        }

        public class Builder
        {
            public Builder Force(bool value)
            {
                _force = value;
                return this;
            }

            public Builder Reboot(bool value)
            {
                _reboot = value;
                return this;
            }

            public Builder Timeout(TimeSpan value)
            {
                _timeout = (uint)value.TotalSeconds;
                return this;
            }

            public Builder Message(string value)
            {
                _message = value;
                return this;
            }

            public NowMsgSystemShutdown Build()
            {
                return new NowMsgSystemShutdown()
                {
                    Force = _force,
                    Reboot = _reboot,
                    _timeout = _timeout,
                    Message = _message,
                };
            }

            private bool _force = false;
            private bool _reboot = false;
            private uint _timeout = 0;
            private string _message = "";
        }

        private const ushort FlagForce = 0x0001;
        private const ushort FlagReboot = 0x0002;

        /// <summary>
        /// Enable force shutdown.
        /// </summary>
        public bool Force { get; private init; } = false;

        /// <summary>
        /// Reboot after shutdown.
        /// </summary>
        public bool Reboot { get; private init; } = false;

        /// <summary>
        /// Shutdown timeout.
        /// </summary>
        public TimeSpan Timeout => TimeSpan.FromSeconds((double)_timeout);

        /// <summary>
        /// Message to display before shutdown.
        /// </summary>
        public string Message { get; private init; } = "";

        private uint _timeout = 0;
    }
}