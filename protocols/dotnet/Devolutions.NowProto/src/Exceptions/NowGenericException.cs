namespace Devolutions.NowProto.Exceptions
{
    /// <summary>
    /// Generic/custom exception deserialized from NOW-proto message.
    /// </summary>
    public class NowGenericException(uint code, string? message)
        : NowStatusException(GetDisplayMessage(code, message), NowErrorKind.Generic, code, message)
    {
        private static string GetDisplayMessage(uint code, string? message)
        {
            return message != null
                ? $"Generic error(code={code}): {message}."
                : $"Generic error(code={code}).";
        }
    }
}