namespace Devolutions.NowProto.Exceptions
{
    /// <summary>
    /// NOW protocol-related exception.
    /// </summary>
    public class NowProtocolException(NowProtocolErrorCode code)
        : NowStatusException(GetDisplayMessage(code), NowErrorKind.Now, (ushort)code, null)
    {
        private static string GetDisplayMessage(NowProtocolErrorCode code)
        {
            return code switch
            {
                NowProtocolErrorCode.InUse => "Resource is already in use.",
                NowProtocolErrorCode.InvalidRequest => "Invalid request.",
                NowProtocolErrorCode.Aborted => "Operation has been aborted.",
                NowProtocolErrorCode.NotFound => "Resource not found.",
                NowProtocolErrorCode.AccessDenied => "Access denied.",
                NowProtocolErrorCode.Internal => "Internal error.",
                NowProtocolErrorCode.NotImplemented => "Operation is not implemented.",
                NowProtocolErrorCode.ProtocolVersion => "Incompatible protocol versions.",
                _ => $"Unknown NOW-proto error (code={code})."
            };
        }

        /// <summary>
        /// Get NOW-PROTO error code.
        /// </summary>
        public NowProtocolErrorCode NowProtocolError => (NowProtocolErrorCode)Code;
    }
}