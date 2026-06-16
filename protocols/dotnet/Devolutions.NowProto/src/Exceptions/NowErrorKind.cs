namespace Devolutions.NowProto.Exceptions
{
    public enum NowErrorKind : ushort
    {
        /// <summary>
        /// `code` value is undefined and could be ignored.
        ///
        /// NOW-PROTO: NOW_STATUS_ERROR_KIND_GENERIC
        /// </summary>
        Generic = 0x0000,
        /// <summary>
        /// `code` contains NowProto-defined error code (see `NOW_STATUS_ERROR_KIND_NOW`).
        ///
        /// NOW-PROTO: NOW_STATUS_ERROR_KIND_NOW
        /// </summary>
        Now = 0x0001,
        /// <summary>
        /// `code` field contains Windows error code.
        ///
        /// NOW-PROTO: NOW_STATUS_ERROR_KIND_WINAPI
        /// </summary>
        WinApi = 0x0002,
        /// <summary>
        /// `code` field contains Unix error code.
        ///
        /// NOW-PROTO: NOW_STATUS_ERROR_KIND_UNIX
        /// </summary>
        Unix = 0x0003,
    }
}