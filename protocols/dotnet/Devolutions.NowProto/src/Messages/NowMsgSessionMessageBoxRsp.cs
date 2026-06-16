using Devolutions.NowProto.Exceptions;
using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_SESSION_MSGBOX_RSP_MSG is a message sent in response to NOW_SESSION_MSGBOX_REQ_MSG if
    /// the NOW_MSGBOX_FLAG_RESPONSE has been set, and contains the result from the message box dialog.
    ///
    /// NOW_PROTO: NOW_SESSION_MSGBOX_RSP_MSG
    /// </summary>
    public class NowMsgSessionMessageBoxRsp : INowSerialize, INowDeserialize<NowMsgSessionMessageBoxRsp>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassSession;
        public static byte TypeMessageKind => 0x04; // NOW-PROTO: NOW_SESSION_MSGBOX_RSP_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassSession;
        byte INowMessage.MessageKind => 0x04;

        // -- INowSerialize --
        ushort INowSerialize.Flags => 0;
        uint INowSerialize.BodySize => FixedPartSize + _status.Size;

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(RequestId);
            cursor.WriteUint32Le((uint)_response);
            _status.Serialize(cursor);
        }

        // -- INowDeserialize --

        static NowMsgSessionMessageBoxRsp INowDeserialize<NowMsgSessionMessageBoxRsp>.Deserialize(ushort flags, NowReadCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var requestId = cursor.ReadUInt32Le();
            var response = (MessageBoxResponse)cursor.ReadUInt32Le();
            var status = NowStatus.Deserialize(cursor);

            return new NowMsgSessionMessageBoxRsp(requestId, response, status);
        }

        // -- impl --

        private const uint FixedPartSize = 8; // u32 requestId + u32 response

        public enum MessageBoxResponse : uint
        {
            /// <summary>
            /// OK
            ///
            /// NOW_PROTO: IDOK
            /// </summary>
            Ok = 1,

            /// <summary>
            /// Cancel
            ///
            /// NOW_PROTO: IDCANCEL
            /// </summary>
            Cancel = 2,

            /// <summary>
            /// Abort
            ///
            /// NOW_PROTO: IDABORT
            /// </summary>
            Abort = 3,

            /// <summary>
            /// Retry
            ///
            /// NOW_PROTO: IDRETRY
            /// </summary>
            Retry = 4,

            /// <summary>
            /// Ignore
            ///
            /// NOW_PROTO: IDIGNORE
            /// </summary>
            Ignore = 5,

            /// <summary>
            /// Yes
            ///
            /// NOW_PROTO: IDYES
            /// </summary>
            Yes = 6,

            /// <summary>
            /// No
            ///
            /// NOW_PROTO: IDNO
            /// </summary>
            No = 7,

            /// <summary>
            /// Try Again
            ///
            /// NOW_PROTO: IDTRYAGAIN
            /// </summary>
            TryAgain = 10,

            /// <summary>
            /// Continue
            ///
            /// NOW_PROTO: IDCONTINUE
            /// </summary>
            Continue = 11,

            /// <summary>
            /// Timeout
            ///
            /// NOW_PROTO: IDTIMEOUT
            /// </summary>
            Timeout = 32000
        }

        internal NowMsgSessionMessageBoxRsp(uint requestId, MessageBoxResponse response, NowStatus status)
        {
            RequestId = requestId;
            _response = response;
            _status = status;
        }

        public static NowMsgSessionMessageBoxRsp Success(uint requestId, MessageBoxResponse response)
        {
            return new NowMsgSessionMessageBoxRsp(requestId, response, NowStatus.Success());
        }

        public static NowMsgSessionMessageBoxRsp Error(uint requestId, NowStatusException exception)
        {
            return new NowMsgSessionMessageBoxRsp(
                requestId,
                (MessageBoxResponse)0,
                NowStatus.Error(exception)
            );
        }

        public bool IsSuccess => _status.IsSuccess;

        public MessageBoxResponse GetResponseOrThrow()
        {
            _status.ThrowIfError();
            return _response;
        }

        public uint RequestId { get; private init; }

        private readonly MessageBoxResponse _response;
        private readonly NowStatus _status;
    }
}