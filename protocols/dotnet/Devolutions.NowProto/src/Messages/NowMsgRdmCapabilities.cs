using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// NOW-PROTO: NOW_RDM_SYNC_FLAGS
    /// </summary>
    [Flags]
    public enum NowRdmSyncFlags : uint
    {
        /// <summary>
        /// None flag.
        /// </summary>
        None = 0x00000000,

        /// <summary>
        /// RDM application is available. Only sent by the server to the client.
        /// NOW-PROTO: NOW_RDM_SYNC_FLAG_APP_AVAILABLE
        /// </summary>
        AppAvailable = 0x00000001,
    }

    /// <summary>
    /// The NOW_RDM_CAPABILITIES_MSG is used to synchronize client and server capabilities, such as system time, RDM versions, etc.
    /// The client sends this message to the server expecting an immediate response back, such that important system clock differences can be detected.
    ///
    /// NOW-PROTO: NOW_RDM_CAPABILITIES_MSG
    /// </summary>
    public class NowMsgRdmCapabilities : INowSerialize, INowDeserialize<NowMsgRdmCapabilities>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassRdm;
        public static byte TypeMessageKind => (byte)RdmMessageKind.Capabilities;

        byte INowMessage.MessageClass => NowMessage.ClassRdm;
        byte INowMessage.MessageKind => (byte)RdmMessageKind.Capabilities;

        // -- INowDeserialize --

        static NowMsgRdmCapabilities INowDeserialize<NowMsgRdmCapabilities>.Deserialize(
            ushort flags,
            NowReadCursor cursor
        )
        {
            cursor.EnsureEnoughBytes(FixedPartSize);

            var timestamp = cursor.ReadUInt64Le();
            var syncFlags = (NowRdmSyncFlags)cursor.ReadUInt32Le();
            var rdmVersion = cursor.ReadVarStr();
            var versionExtra = cursor.ReadVarStr();

            return new NowMsgRdmCapabilities
            {
                Timestamp = timestamp,
                SyncFlags = syncFlags,
                RdmVersion = rdmVersion,
                VersionExtra = versionExtra,
            };
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags => 0;

        uint INowSerialize.BodySize => FixedPartSize
                                     + NowVarStr.LengthOf(RdmVersion)
                                     + NowVarStr.LengthOf(VersionExtra);

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint64Le(Timestamp);
            cursor.WriteUint32Le((uint)SyncFlags);
            cursor.WriteVarStr(RdmVersion);
            cursor.WriteVarStr(VersionExtra);
        }

        // -- impl --

        private const uint FixedPartSize = 12; // 8 + 4 bytes

        /// <summary>
        /// Hide public default constructor.
        /// </summary>
        internal NowMsgRdmCapabilities() { }

        /// <summary>
        /// The system UTC time, in seconds since the Unix epoch, encoded as unsigned 64-bit integer.
        /// This is the equivalent of (ulong)[DateTimeOffset]::UtcNow.ToUnixTimeSeconds() in .NET or `date +%s` in Linux.
        /// </summary>
        public ulong Timestamp { get; private init; } = 0;

        /// <summary>
        /// The synchronization flags.
        /// </summary>
        public NowRdmSyncFlags SyncFlags { get; private init; } = NowRdmSyncFlags.None;

        /// <summary>
        /// The RDM version string with 4 parts like "2025.X.Y.Z", encoded as a NOW_VARSTR.
        /// This value is empty if RDM is not available.
        /// </summary>
        public string RdmVersion { get; private init; } = "";

        /// <summary>
        /// A string field reserved for extra version information with no predefined format.
        /// Unused for now, leave empty.
        /// </summary>
        public string VersionExtra { get; private init; } = "";

        /// <summary>
        /// Check if the RDM application is available.
        /// </summary>
        public bool IsAppAvailable => SyncFlags.HasFlag(NowRdmSyncFlags.AppAvailable);

        /// <summary>
        /// Builder for NowMsgRdmCapabilities with required parameters.
        /// </summary>
        public class Builder(ulong timestamp, string rdmVersion)
        {
            public Builder VersionExtra(string versionExtra)
            {
                _versionExtra = versionExtra ?? "";
                return this;
            }

            public Builder WithAppAvailable()
            {
                _syncFlags |= NowRdmSyncFlags.AppAvailable;
                return this;
            }

            public NowMsgRdmCapabilities Build()
            {
                return new NowMsgRdmCapabilities
                {
                    Timestamp = timestamp,
                    SyncFlags = _syncFlags,
                    RdmVersion = rdmVersion ?? "",
                    VersionExtra = _versionExtra,
                };
            }

            private NowRdmSyncFlags _syncFlags = NowRdmSyncFlags.None;
            private string _versionExtra = "";
        }
    }
}