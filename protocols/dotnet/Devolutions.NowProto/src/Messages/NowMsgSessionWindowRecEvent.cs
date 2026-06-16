using Devolutions.NowProto.Exceptions;
using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// Active window event data.
    /// </summary>
    public class ActiveWindowEventData
    {
        /// <summary>
        /// Process ID of the active window.
        /// </summary>
        public uint ProcessId { get; }

        /// <summary>
        /// Window title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Full path to the executable.
        /// </summary>
        public string ExecutablePath { get; }

        internal ActiveWindowEventData(uint processId, string title, string executablePath)
        {
            ProcessId = processId;
            Title = title;
            ExecutablePath = executablePath;
        }
    }

    /// <summary>
    /// Title changed event data.
    /// </summary>
    public class TitleChangedEventData
    {
        /// <summary>
        /// New window title.
        /// </summary>
        public string Title { get; }

        internal TitleChangedEventData(string title)
        {
            Title = title;
        }
    }

    /// <summary>
    /// Window recording event kind.
    ///
    /// NOW_PROTO: NOW_SESSION_WINDOW_REC_EVENT_MSG msgFlags
    /// </summary>
    public enum WindowRecEventKind
    {
        /// <summary>
        /// Active window changed. Contains the window title, process ID, and executable path.
        ///
        /// NOW-PROTO: NOW_WINDOW_REC_EVENT_ACTIVE_WINDOW
        /// </summary>
        ActiveWindow,

        /// <summary>
        /// Window title changed for the current active window. Contains only the new title.
        ///
        /// NOW-PROTO: NOW_WINDOW_REC_EVENT_TITLE_CHANGED
        /// </summary>
        TitleChanged,

        /// <summary>
        /// No active window.
        ///
        /// NOW-PROTO: NOW_WINDOW_REC_EVENT_NO_ACTIVE_WINDOW
        /// </summary>
        NoActiveWindow,
    }

    [Flags]
    internal enum WindowRecEventFlags : ushort
    {
        ActiveWindow = 0x0001,
        TitleChanged = 0x0002,
        NoActiveWindow = 0x0004,
    }

    /// <summary>
    /// The NOW_SESSION_WINDOW_REC_EVENT_MSG message is sent by the server to notify of window recording
    /// events such as active window changes, title changes, or when no window is active.
    ///
    /// NOW_PROTO: NOW_SESSION_WINDOW_REC_EVENT_MSG
    /// </summary>
    public class NowMsgSessionWindowRecEvent : INowSerialize, INowDeserialize<NowMsgSessionWindowRecEvent>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassSession;
        public static byte TypeMessageKind => (byte)SessionMessageKind.WindowRecEvent;

        byte INowMessage.MessageClass => NowMessage.ClassSession;
        byte INowMessage.MessageKind => (byte)SessionMessageKind.WindowRecEvent;

        // -- INowSerialize --

        ushort INowSerialize.Flags
        {
            get
            {
                return Kind switch
                {
                    WindowRecEventKind.ActiveWindow => (ushort)WindowRecEventFlags.ActiveWindow,
                    WindowRecEventKind.TitleChanged => (ushort)WindowRecEventFlags.TitleChanged,
                    WindowRecEventKind.NoActiveWindow => (ushort)WindowRecEventFlags.NoActiveWindow,
                    _ => 0,
                };
            }
        }

        uint INowSerialize.BodySize
        {
            get
            {
                uint titleSize = Kind switch
                {
                    WindowRecEventKind.ActiveWindow => NowVarStr.LengthOf(_title),
                    WindowRecEventKind.TitleChanged => NowVarStr.LengthOf(_title),
                    _ => NowVarStr.LengthOf(""),
                };

                uint execPathSize = Kind switch
                {
                    WindowRecEventKind.ActiveWindow => NowVarStr.LengthOf(_executablePath),
                    _ => NowVarStr.LengthOf(""),
                };

                return FixedPartSize + titleSize + execPathSize;
            }
        }

        public void SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint64Le(Timestamp);

            uint processId = Kind switch
            {
                WindowRecEventKind.ActiveWindow => _processId,
                _ => 0,
            };
            cursor.WriteUint32Le(processId);

            string title = Kind switch
            {
                WindowRecEventKind.ActiveWindow => _title,
                WindowRecEventKind.TitleChanged => _title,
                _ => "",
            };
            cursor.WriteVarStr(title);

            string execPath = Kind switch
            {
                WindowRecEventKind.ActiveWindow => _executablePath,
                _ => "",
            };
            cursor.WriteVarStr(execPath);
        }

        // -- INowDeserialize --
        public static NowMsgSessionWindowRecEvent Deserialize(ushort flags, NowReadCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var eventFlags = (WindowRecEventFlags)flags;
            var timestamp = cursor.ReadUInt64Le();
            var processId = cursor.ReadUInt32Le();
            var title = cursor.ReadVarStr();
            var executablePath = cursor.ReadVarStr();

            if (eventFlags.HasFlag(WindowRecEventFlags.ActiveWindow))
            {
                return ActiveWindow(timestamp, processId, title, executablePath);
            }
            else if (eventFlags.HasFlag(WindowRecEventFlags.TitleChanged))
            {
                return TitleChanged(timestamp, title);
            }
            else if (eventFlags.HasFlag(WindowRecEventFlags.NoActiveWindow))
            {
                return NoActiveWindow(timestamp);
            }
            else
            {
                throw new NowDecodeException(NowDecodeException.ErrorKind.InvalidWindowRecEventFlags);
            }
        }

        // -- impl --

        private const uint FixedPartSize = 12; // u64 timestamp + u32 processId

        /// <summary>
        /// Event kind
        /// </summary>
        public WindowRecEventKind Kind { get; }

        /// <summary>
        /// The system UTC time, in seconds since the Unix epoch
        /// </summary>
        public ulong Timestamp { get; }

        private readonly uint _processId;
        private readonly string _title;
        private readonly string _executablePath;

        /// <summary>
        /// Gets active window event data. Throws if Kind is not ActiveWindow.
        /// </summary>
        public ActiveWindowEventData GetActiveWindowData()
        {
            if (Kind != WindowRecEventKind.ActiveWindow)
            {
                throw new InvalidOperationException($"Cannot get ActiveWindowData for event kind {Kind}");
            }
            return new ActiveWindowEventData(_processId, _title, _executablePath);
        }

        /// <summary>
        /// Gets title changed event data. Throws if Kind is not TitleChanged.
        /// </summary>
        public TitleChangedEventData GetTitleChangedData()
        {
            if (Kind != WindowRecEventKind.TitleChanged)
            {
                throw new InvalidOperationException($"Cannot get TitleChangedData for event kind {Kind}");
            }
            return new TitleChangedEventData(_title);
        }

        private NowMsgSessionWindowRecEvent(
            WindowRecEventKind kind,
            ulong timestamp,
            uint processId,
            string title,
            string executablePath)
        {
            Kind = kind;
            Timestamp = timestamp;
            _processId = processId;
            _title = title;
            _executablePath = executablePath;
        }

        /// <summary>
        /// Creates an ActiveWindow event
        /// </summary>
        public static NowMsgSessionWindowRecEvent ActiveWindow(ulong timestamp, uint processId, string title, string executablePath)
        {
            return new NowMsgSessionWindowRecEvent(
                WindowRecEventKind.ActiveWindow,
                timestamp,
                processId,
                title,
                executablePath);
        }

        /// <summary>
        /// Creates a TitleChanged event
        /// </summary>
        public static NowMsgSessionWindowRecEvent TitleChanged(ulong timestamp, string title)
        {
            return new NowMsgSessionWindowRecEvent(
                WindowRecEventKind.TitleChanged,
                timestamp,
                0,
                title,
                "");
        }

        /// <summary>
        /// Creates a NoActiveWindow event
        /// </summary>
        public static NowMsgSessionWindowRecEvent NoActiveWindow(ulong timestamp)
        {
            return new NowMsgSessionWindowRecEvent(
                WindowRecEventKind.NoActiveWindow,
                timestamp,
                0,
                "",
                "");
        }
    }
}