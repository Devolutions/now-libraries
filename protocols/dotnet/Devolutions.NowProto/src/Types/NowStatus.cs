using Devolutions.NowProto.Exceptions;

namespace Devolutions.NowProto.Types
{
    /// <summary>
    /// Channel or session operation status.
    ///
    /// NOW-PROTO: NOW_STATUS
    /// </summary>
    internal class NowStatus
    {
        [Flags]
        private enum Flags : ushort
        {
            None = 0x0000,

            /// <summary>
            /// This flag set for all error statuses. If flag is not set, operation was successful.
            ///
            /// NOW-PROTO: NOW_STATUS_ERROR
            /// </summary>
            FlagError = 0x0001,

            /// <summary>
            /// Set if `errorMessage` contains optional error message.
            ///
            /// NOW-PROTO: NOW_STATUS_ERROR_MESSAGE
            /// </summary>
            FlagErrorMessage = 0x0002,
        }

        private const uint FixedPartSize = 8; /* u16 flags + u16 kind + u32 status code */

        private NowStatus(bool isError, NowErrorKind kind, uint code, string? message)
        {
            _isError = isError;
            _kind = kind;
            _code = code;
            _message = message;
        }

        /// <summary>
        /// Create new success status.
        /// </summary>
        public static NowStatus Success()
        {
            return new NowStatus(false, NowErrorKind.Generic, 0, null);
        }

        /// <summary>
        /// Create new error status.
        /// </summary>
        public static NowStatus Error(NowStatusException exception)
        {
            return new NowStatus(true, exception.Kind, exception.Code, exception.NowErrorMessage);
        }

        internal static NowStatus Deserialize(NowReadCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);

            var flags = (Flags)cursor.ReadUInt16Le();
            var kind = (NowErrorKind)cursor.ReadUInt16Le();
            var code = cursor.ReadUInt32Le();
            var message = cursor.ReadVarStr();

            var isError = flags.HasFlag(Flags.FlagError);
            var hasErrorMessage = flags.HasFlag(Flags.FlagErrorMessage);

            return hasErrorMessage
                ? new NowStatus(isError, kind, code, message)
                : new NowStatus(isError, kind, code, null);
        }

        internal void Serialize(NowWriteCursor cursor)
        {
            var flags = Flags.None;

            if (_isError)
            {
                flags |= Flags.FlagError;
            }

            if (_message != null)
            {
                flags |= Flags.FlagErrorMessage;
            }

            cursor.WriteUint16Le((ushort)flags);
            cursor.WriteUint16Le((ushort)_kind);
            cursor.WriteUint32Le(_code);
            cursor.WriteVarStr(_message ?? "");
        }

        internal bool IsSuccess => !_isError;

        /// <summary>
        /// Throws an exception if the status is an error.
        /// </summary>
        /// <exception cref="NowProtocolException"></exception>
        /// <exception cref="NowWinApiException"></exception>
        /// <exception cref="NowUnixException"></exception>
        /// <exception cref="NowGenericException"></exception>
        public void ThrowIfError()
        {
            if (!_isError)
            {
                return;
            }

            throw _kind switch
            {
                NowErrorKind.Generic => new NowGenericException(_code, _message),
                NowErrorKind.Now => new NowProtocolException((NowProtocolErrorCode)_code),
                NowErrorKind.WinApi => new NowWinApiException(_code),
                NowErrorKind.Unix => new NowUnixException(_code),
                _ => new NowUnknownException(_kind, _code, _message),
            };
        }

        public uint Size => FixedPartSize + NowVarStr.LengthOf(_message ?? "");

        private readonly bool _isError;
        private readonly NowErrorKind _kind;
        private readonly uint _code;
        private readonly string? _message;
    }
}