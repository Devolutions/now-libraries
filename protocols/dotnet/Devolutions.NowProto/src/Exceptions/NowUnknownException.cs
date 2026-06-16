namespace Devolutions.NowProto.Exceptions
{
    /// <summary>
    /// Unknown error deserialized from NOW-proto message.
    /// </summary>
    public class NowUnknownException : NowStatusException
    {
        internal NowUnknownException(NowErrorKind kind, uint code, string? message)
            : base(GetDisplayMessage((ushort)kind, code, message), kind, code, message)
        {
        }

        private static string GetDisplayMessage(ushort kind, uint code, string? message)
        {
            return message != null
                ? $"Unknown error (code={code}, kind={kind}): {message}."
                : $"Unknown error (code={code}, kind={kind}).";
        }
    }
}