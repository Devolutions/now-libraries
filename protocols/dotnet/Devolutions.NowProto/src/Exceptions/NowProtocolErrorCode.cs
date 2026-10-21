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
        /// <summary>
        /// Elevation was requested and refused: the interactive user declined consent, the consent
        /// prompt expired, or policy denies elevation for the account.
        ///
        /// NOW-PROTO: NOW_CODE_ELEVATION_DENIED
        /// </summary>
        ElevationDenied = 0x0009,
        /// <summary>
        /// Elevation was requested and is not possible for this request: the account has no
        /// elevated token, or the server cannot elevate the requested execution style.
        ///
        /// NOW-PROTO: NOW_CODE_ELEVATION_UNAVAILABLE
        /// </summary>
        ElevationUnavailable = 0x000A,
    }
}