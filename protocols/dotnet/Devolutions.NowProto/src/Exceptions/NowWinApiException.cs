namespace Devolutions.NowProto.Exceptions
{
    /// <summary>
    /// WinAPI error code.
    /// </summary>
    public class NowWinApiException(uint code)
        : NowStatusException($"WinAPI error code {code}.", NowErrorKind.WinApi, code, null)
    {
        public uint WinApiError => Code;
    }
}