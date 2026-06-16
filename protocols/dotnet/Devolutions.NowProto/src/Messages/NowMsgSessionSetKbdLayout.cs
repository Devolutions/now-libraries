using Devolutions.NowProto.Exceptions;
using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_SESSION_SET_KBD_LAYOUT_MSG message is used to set the keyboard layout for the active
    /// foreground window. The request is fire-and-forget, invalid layout identifiers are ignored.
    ///
    /// NOW_PROTO: NOW_SESSION_SET_KBD_LAYOUT_MSG
    /// </summary>
    public class NowMsgSessionSetKbdLayout : INowSerialize, INowDeserialize<NowMsgSessionSetKbdLayout>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassSession;
        public static byte TypeMessageKind => 0x05; // NOW-PROTO: NOW_SESSION_SET_KBD_LAYOUT_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassSession;
        byte INowMessage.MessageKind => 0x05;

        // -- INowSerialize --

        ushort INowSerialize.Flags => LayoutOption switch
        {
            SetKbdLayoutOption.Next => (ushort)MsgFlags.NextLayout,
            SetKbdLayoutOption.Prev => (ushort)MsgFlags.PrevLayout,
            _ => 0
        };

        uint INowSerialize.BodySize => NowVarStr.LengthOf(_layout);

        public void SerializeBody(NowWriteCursor cursor)
        {
            cursor.WriteVarStr(_layout);
        }

        // -- INowDeserialize --
        public static NowMsgSessionSetKbdLayout Deserialize(ushort flags, NowReadCursor cursor)
        {
            var headerFlags = (MsgFlags)flags;
            var layout = cursor.ReadVarStr();

            if (headerFlags.HasFlag(MsgFlags.NextLayout) && headerFlags.HasFlag(MsgFlags.PrevLayout))
            {
                throw new NowDecodeException(NowDecodeException.ErrorKind.InvalidKbdLayoutFlags);
            }

            var layoutOption = headerFlags switch
            {
                MsgFlags.NextLayout => SetKbdLayoutOption.Next,
                MsgFlags.PrevLayout => SetKbdLayoutOption.Prev,
                _ => SetKbdLayoutOption.Specific
            };

            return new NowMsgSessionSetKbdLayout(layoutOption, layout);
        }


        // -- impl --

        [Flags]
        private enum MsgFlags : ushort
        {
            /// <summary>
            /// Switches to next keyboard layout. kbdLayoutId field should contain empty string.
            /// Conflicts with NOW_SET_KBD_LAYOUT_FLAG_PREV.
            ///
            /// NOW_PROTO: NOW_SET_KBD_LAYOUT_FLAG_NEXT
            /// </summary>
            NextLayout = 0x0001,

            /// <summary>
            /// Switches to previous keyboard layout. kbdLayoutId field should contain empty string.
            /// Conflicts with NOW_SET_KBD_LAYOUT_FLAG_NEXT.
            ///
            /// NOW_PROTO: NOW_SET_KBD_LAYOUT_FLAG_PREV
            /// </summary>
            PrevLayout = 0x0002,
        }

        public enum SetKbdLayoutOption
        {
            Next,
            Prev,
            Specific,
        }

        /// <summary>
        /// Creates a new request to switch to the next keyboard layout.
        /// </summary>
        public static NowMsgSessionSetKbdLayout Next()
        {
            return new NowMsgSessionSetKbdLayout(SetKbdLayoutOption.Next, "");
        }

        /// <summary>
        /// Creates a new request to switch to the previous keyboard layout.
        /// </summary>
        public static NowMsgSessionSetKbdLayout Prev()
        {
            return new NowMsgSessionSetKbdLayout(SetKbdLayoutOption.Prev, "");
        }


        /// <summary>
        /// Creates a new request to switch to a specific keyboard layout.
        /// </summary>
        public static NowMsgSessionSetKbdLayout Specific(string layout)
        {
            return new NowMsgSessionSetKbdLayout(SetKbdLayoutOption.Specific, layout);
        }

        private NowMsgSessionSetKbdLayout(SetKbdLayoutOption layoutOption, string layout)
        {
            LayoutOption = layoutOption;
            _layout = layout;
        }

        /// <summary>
        /// Get specified layout change request kind.
        /// </summary>
        public SetKbdLayoutOption LayoutOption { get; }

        /// <summary>
        /// Get specific layout identifier if LayoutOption set to Specific.
        /// Returns null otherwise.
        /// </summary>
        public string? Layout => LayoutOption == SetKbdLayoutOption.Specific ? _layout : null;

        private readonly string _layout;
    }
}