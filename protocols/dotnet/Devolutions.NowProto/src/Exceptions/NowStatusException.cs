namespace Devolutions.NowProto.Exceptions
{
    /// <summary>
    /// Base class for all exceptions produced from NOW_STATUS structure.
    /// </summary>
    public class NowStatusException : NowException
    {
        internal NowStatusException(string displayMessage, NowErrorKind kind, UInt32 code, string? nowErrorMessage)
            : base(displayMessage)
        {
            Kind = kind;
            Code = code;
            NowErrorMessage = nowErrorMessage;
        }

        public NowErrorKind Kind { get; }
        public uint Code { get; }
        public string? NowErrorMessage { get; }
    }
}