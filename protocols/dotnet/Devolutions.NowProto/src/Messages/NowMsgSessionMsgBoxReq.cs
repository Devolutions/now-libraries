using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_SESSION_MSGBOX_REQ_MSG is used to show a message box in the user session, similar to
    /// what the [WTSSendMessage function](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtssendmessagew)
    /// does.
    ///
    /// NOW_PROTO: NOW_SESSION_MSGBOX_REQ_MSG
    /// </summary>
    public class NowMsgSessionMessageBoxReq : INowSerialize, INowDeserialize<NowMsgSessionMessageBoxReq>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassSession;
        public static byte TypeMessageKind => 0x03; // NOW-PROTO: NOW_SESSION_MSGBOX_REQ_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassSession;
        byte INowMessage.MessageKind => 0x03;

        // -- INowSerialize --

        ushort INowSerialize.Flags => (ushort)(
            ((Title != null) ? MsgFlags.Title : MsgFlags.None)
            | ((Style != null) ? MsgFlags.Style : MsgFlags.None)
            | ((_timeout != null) ? MsgFlags.Timeout : MsgFlags.None)
            | ((WaitForResponse) ? MsgFlags.Response : MsgFlags.None)
        );

        uint INowSerialize.BodySize =>
            FixedPartSize
            + NowVarStr.LengthOf(Title ?? "")
            + NowVarStr.LengthOf(Message);

        public void SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(RequestId);
            cursor.WriteUint32Le((uint)(Style ?? MessageBoxStyle.Ok));
            cursor.WriteUint32Le(_timeout ?? 0);
            cursor.WriteVarStr(Title ?? "");
            cursor.WriteVarStr(Message);
        }

        // -- INowDeserialize --
        public static NowMsgSessionMessageBoxReq Deserialize(ushort flags, NowReadCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var headerFlags = (MsgFlags)flags;
            var requestId = cursor.ReadUInt32Le();
            var style = (MessageBoxStyle)cursor.ReadUInt32Le();
            var timeout = cursor.ReadUInt32Le();
            var title = cursor.ReadVarStr();
            var message = cursor.ReadVarStr();

            return new NowMsgSessionMessageBoxReq()
            {
                RequestId = requestId,
                Message = message,
                Style = headerFlags.HasFlag(MsgFlags.Style) ? style : null,
                _timeout = headerFlags.HasFlag(MsgFlags.Timeout) ? timeout : null,
                Title = headerFlags.HasFlag(MsgFlags.Title) ? title : null,
                WaitForResponse = headerFlags.HasFlag(MsgFlags.Response),
            };
        }


        // -- impl --

        private const uint FixedPartSize = 12; // u32 requestId + u32 style + u32 timeout

        [Flags]
        private enum MsgFlags : ushort
        {
            None = 0x0000,

            /// The title field contains non-default value.
            ///
            /// NOW_PROTO: NOW_SESSION_MSGBOX_FLAG_TITLE
            Title = 0x0001,
            // The style field contains non-default value.
            //
            // NOW_PROTO: NOW_SESSION_MSGBOX_FLAG_STYLE
            Style = 0x0002,

            // The timeout field contains non-default value.
            //
            // NOW_PROTO: NOW_SESSION_MSGBOX_FLAG_TIMEOUT
            Timeout = 0x0004,

            // A response message is expected (don't fire and forget)
            //
            // NOW_PROTO: NOW_SESSION_MSGBOX_FLAG_RESPONSE
            Response = 0x0008,
        }


        public enum MessageBoxStyle : uint
        {
            Ok = 0x00000000,
            OkCancel = 0x00000001,
            AbortRetryIgnore = 0x00000002,
            YesNoCancel = 0x00000003,
            YesNo = 0x00000004,
            RetryCancel = 0x00000005,
            CancelTryContinue = 0x00000006,
            Help = 0x00004000,
        }

        public class Builder(uint requestId, string message)
        {
            public Builder Style(MessageBoxStyle value)
            {
                _style = value;
                return this;
            }

            public Builder Timeout(TimeSpan value)
            {
                _timeout = (uint)value.TotalSeconds;
                return this;
            }

            public Builder Title(string value)
            {
                _title = value;
                return this;
            }

            public Builder WithResponse()
            {
                _waitForResponse = true;
                return this;
            }

            public NowMsgSessionMessageBoxReq Build()
            {
                return new NowMsgSessionMessageBoxReq()
                {
                    RequestId = _requestId,
                    Message = _message,
                    Style = _style,
                    _timeout = _timeout,
                    Title = _title,
                    WaitForResponse = _waitForResponse,
                };
            }

            private readonly uint _requestId = requestId;
            private readonly string _message = message;
            private MessageBoxStyle? _style = null;
            private uint? _timeout = null;
            private string? _title = null;
            private bool _waitForResponse = false;
        }

        /// <summary>
        /// Make constructor internal to avoid non-meaningful message box requests.
        /// </summary>
        internal NowMsgSessionMessageBoxReq()
        {

        }

        /// <summary>
        /// Message box request id.
        /// </summary>
        public uint RequestId { get; set; } = 0;

        /// <summary>
        /// Message box text.
        /// </summary>
        public string Message { get; private init; } = "";

        /// <summary>
        /// Message box style. Null if default style is used.
        /// </summary>
        public MessageBoxStyle? Style { get; private init; }

        /// <summary>
        /// Message box timeout in seconds. Null if no timeout.
        /// </summary>
        public TimeSpan? Timeout => (_timeout == null)
            ? null
            : TimeSpan.FromSeconds((double)_timeout);

        /// <summary>
        /// Message box title text.
        /// </summary>
        public string? Title { get; private init; }

        /// <summary>
        /// Specify if the message box request should wait for a response.
        /// </summary>
        public bool WaitForResponse { get; private init; }

        private uint? _timeout = 0;
    }
}