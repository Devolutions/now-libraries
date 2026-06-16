namespace Devolutions.NowProto.Exceptions
{
    /// <summary>
    /// Unix error code.
    /// </summary>
    public class NowUnixException(uint code)
        : NowStatusException($"Unix error code {code}.", NowErrorKind.Unix, code, null)
    {
        public uint UnixError => Code;
    }
}