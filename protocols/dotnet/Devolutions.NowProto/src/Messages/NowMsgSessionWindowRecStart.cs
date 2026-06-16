using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    [Flags]
    internal enum WindowRecStartFlags : ushort
    {
        None = 0x0000,
        TrackTitleChange = 0x0001,
    }

    /// <summary>
    /// The NOW_SESSION_WINDOW_REC_START_MSG message is used to start window recording, which tracks
    /// active window changes and title updates.
    ///
    /// NOW_PROTO: NOW_SESSION_WINDOW_REC_START_MSG
    /// </summary>
    public class NowMsgSessionWindowRecStart : INowSerialize, INowDeserialize<NowMsgSessionWindowRecStart>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassSession;
        public static byte TypeMessageKind => (byte)SessionMessageKind.WindowRecStart;

        byte INowMessage.MessageClass => NowMessage.ClassSession;
        byte INowMessage.MessageKind => (byte)SessionMessageKind.WindowRecStart;

        // -- INowSerialize --

        ushort INowSerialize.Flags => (ushort)_flags;

        uint INowSerialize.BodySize => FixedPartSize;

        public void SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(PollInterval);
        }

        // -- INowDeserialize --
        public static NowMsgSessionWindowRecStart Deserialize(ushort flags, NowReadCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var startFlags = (WindowRecStartFlags)flags;
            var pollInterval = cursor.ReadUInt32Le();
            var trackTitleChange = startFlags.HasFlag(WindowRecStartFlags.TrackTitleChange);

            return new NowMsgSessionWindowRecStart(pollInterval, trackTitleChange);
        }

        // -- impl --

        private const uint FixedPartSize = 4; // u32 pollInterval
        private readonly WindowRecStartFlags _flags;

        /// <summary>
        /// Interval in milliseconds for polling window changes.
        /// Set to 0 to use the host's default poll interval.
        /// </summary>
        public uint PollInterval { get; }

        /// <summary>
        /// Whether window title change tracking is enabled.
        /// </summary>
        public bool IsTrackTitleChange => _flags.HasFlag(WindowRecStartFlags.TrackTitleChange);

        public NowMsgSessionWindowRecStart(uint pollInterval, bool trackTitleChange = false)
        {
            PollInterval = pollInterval;
            _flags = trackTitleChange ? WindowRecStartFlags.TrackTitleChange : WindowRecStartFlags.None;
        }
    }
}