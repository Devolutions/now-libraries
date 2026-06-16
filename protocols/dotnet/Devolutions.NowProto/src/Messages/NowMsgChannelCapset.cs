using Devolutions.NowProto.Capabilities;
using Devolutions.NowProto.Exceptions;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// This message is first set by the client side, to advertise capabilities.
    /// Received client message should be downgraded by the server (remove non-intersecting
    /// capabilities) and sent back to the client at the start of DVC channel communications.
    /// DVC channel should be closed if protocol versions are not compatible.
    ///
    /// `Default` implementation returns capabilities with empty capability sets and no heartbeat
    /// interval set. Proto version is set to NowProtoVersion::CURRENT by default. TODO: fix docs
    ///
    /// NOW-PROTO: NOW_CHANNEL_CAPSET_MSG
    /// </summary>
    public class NowMsgChannelCapset : INowSerialize, INowDeserialize<NowMsgChannelCapset>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassChannel;
        public static byte TypeMessageKind => 0x01; // NOW-PROTO: NOW_EXEC_CAPSET_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassChannel;
        byte INowMessage.MessageKind => 0x01;

        // -- INowDeserialize --

        static NowMsgChannelCapset INowDeserialize<NowMsgChannelCapset>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);

            var msgFlags = (MsgFlags)flags;
            var versionMajor = cursor.ReadUInt16Le(); // Major
            var versionMinor = cursor.ReadUInt16Le(); // Minor
            var version = new NowProtoVersion(versionMajor, versionMinor);
            var systemСapset = (NowCapabilitySystem)cursor.ReadUInt16Le();
            var sessionСapset = (NowCapabilitySession)cursor.ReadUInt16Le();
            var execСapset = (NowCapabilityExec)cursor.ReadUInt16Le();
            var heartbeatInterval = cursor.ReadUInt32Le();

            return new NowMsgChannelCapset
            {
                Version = version,
                SystemCapset = systemСapset,
                SessionCapset = sessionСapset,
                ExecCapset = execСapset,
                _heartbeatInterval = msgFlags.HasFlag(MsgFlags.FlagChannelSetHeartbeat) ? heartbeatInterval : null,
            };
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags => (HeartbeatInterval != null) ? (ushort)MsgFlags.FlagChannelSetHeartbeat : (ushort)0;
        uint INowSerialize.BodySize => FixedPartSize;

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint16Le(Version.Major);
            cursor.WriteUint16Le(Version.Minor);
            cursor.WriteUint16Le((ushort)SystemCapset);
            cursor.WriteUint16Le((ushort)SessionCapset);
            cursor.WriteUint16Le((ushort)ExecCapset);
            cursor.WriteUint32Le(_heartbeatInterval ?? (uint)0);
        }

        // -- impl ---

        private const uint FixedPartSize = 14;

        [Flags]
        private enum MsgFlags : ushort
        {
            /// <summary>
            /// Set if heartbeat specify channel heartbeat interval.
            ///
            /// NOW-PROTO: NOW_CHANNEL_SET_HEARTBEAT
            /// </summary>
            FlagChannelSetHeartbeat = 0x0001,
        }

        public class Builder
        {
            public Builder HeartbeatInterval(TimeSpan interval)
            {
                // Sanity check: Limit minimum heartbeat interval to 5 seconds.
                const uint minHeartbeatInterval = 5;

                // Sanity check: Limit maximum heartbeat interval to 24 hours.
                const uint maxHeartbeatInterval = 60 * 60 * 24;

                if (interval.TotalSeconds is < minHeartbeatInterval or > maxHeartbeatInterval)
                {
                    throw new NowEncodeException(NowEncodeException.ErrorKind.HeartbeatOutOfRange);
                }

                _heartbeatInterval = (uint)interval.TotalSeconds;

                return this;
            }

            public Builder SystemCapset(NowCapabilitySystem capset)
            {
                _systemСapset = capset;
                return this;
            }

            public Builder SessionCapset(NowCapabilitySession capset)
            {
                _sessionСapset = capset;
                return this;
            }

            public Builder ExecCapset(NowCapabilityExec capset)
            {
                _execСapset = capset;
                return this;
            }

            public NowMsgChannelCapset Build()
            {
                return new NowMsgChannelCapset
                {
                    Version = NowProtoVersion.Current,
                    SystemCapset = _systemСapset,
                    SessionCapset = _sessionСapset,
                    ExecCapset = _execСapset,
                    _heartbeatInterval = _heartbeatInterval,
                };
            }

            private NowCapabilitySystem _systemСapset = NowCapabilitySystem.None;
            private NowCapabilitySession _sessionСapset = NowCapabilitySession.None;
            private NowCapabilityExec _execСapset = NowCapabilityExec.None;
            private uint? _heartbeatInterval;
        }

        /// <summary>
        /// Reported peer protocol version.
        /// </summary>
        public NowProtoVersion Version { get; private init; } = NowProtoVersion.Current;

        /// <summary>
        /// Available system capabilities.
        /// </summary>
        public NowCapabilitySystem SystemCapset { get; private init; } = NowCapabilitySystem.None;

        /// <summary>
        /// Available session capabilities.
        /// </summary>
        public NowCapabilitySession SessionCapset { get; private init; } = NowCapabilitySession.None;
        /// <summary>
        /// Available exec capabilities.
        /// </summary>
        public NowCapabilityExec ExecCapset { get; private init; } = NowCapabilityExec.None;

        /// <summary>
        /// Expected heartbeat interval or null if not set.
        /// </summary>
        public TimeSpan? HeartbeatInterval => (_heartbeatInterval != null)
            ? TimeSpan.FromSeconds((double)_heartbeatInterval)
            : null;

        private uint? _heartbeatInterval;
    }
}