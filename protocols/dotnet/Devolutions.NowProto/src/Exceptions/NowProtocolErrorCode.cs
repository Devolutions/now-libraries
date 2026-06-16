namespace Devolutions.NowProto.Exceptions
{
    public enum NowProtocolErrorCode : ushort
    {
        /// <summary>
        /// Resource (e.g. exec session id is already in use).
        ///
        /// NOW-PROTO: NOW_CODE_IN_USE
        /// </summary>
        InUse = 0x0001,
        /// <summary>
        /// Sent request is invalid (e.g. invalid exec request params).
        ///
        /// NOW-PROTO: NOW_CODE_INVALID_REQUEST
        /// </summary>
        InvalidRequest = 0x0002,
        /// <summary>
        /// Operation has been aborted on the server side.
        ///
        /// NOW-PROTO: NOW_CODE_ABORTED
        /// </summary>
        Aborted = 0x0003,
        /// <summary>
        /// Resource not found.
        ///
        /// NOW-PROTO: NOW_CODE_NOT_FOUND
        /// </summary>
        NotFound = 0x0004,
        /// <summary>
        /// Resource can't be accessed.
        ///
        /// NOW-PROTO: NOW_CODE_ACCESS_DENIED
        /// </summary>
        AccessDenied = 0x0005,
        /// <summary>
        /// Internal error.
        ///
        /// NOW-PROTO: NOW_CODE_INTERNAL
        /// </summary>
        Internal = 0x0006,
        /// <summary>
        /// Operation is not implemented on current platform.
        ///
        /// NOW-PROTO: NOW_CODE_NOT_IMPLEMENTED
        /// </summary>
        NotImplemented = 0x0007,
        /// <summary>
        /// Incompatible protocol versions.
        ///
        /// NOW-PROTO: NOW_CODE_PROTOCOL_VERSION
        /// </summary>
        ProtocolVersion = 0x0008,
    }
}