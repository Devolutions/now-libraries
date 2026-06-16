using System.Diagnostics;

namespace Devolutions.NowProto.Exceptions
{
    /// <summary>
    /// NOW-proto encoding exception.
    /// </summary>
    public class NowEncodeException : NowException
    {
        internal NowEncodeException(ErrorKind kind)
            : base(KindToMessage(kind))
        {
            Kind = kind;
        }

        public enum ErrorKind
        {
            NotEnoughBytes,
            VarU32OutOfRange,
            HeartbeatOutOfRange,
            InvalidApartmentState,
        }

        private static string KindToMessage(ErrorKind kind)
        {
            return kind switch
            {
                ErrorKind.NotEnoughBytes => "Not enough bytes to encode the value.",
                ErrorKind.VarU32OutOfRange => "Failed to encode VarU32, value out of range.",
                ErrorKind.HeartbeatOutOfRange => "Failed to encode heartbeat interval, value out of range.",
                ErrorKind.InvalidApartmentState => "Invalid apartment state.",
                _ => throw new UnreachableException("Should not be constructed with invalid kind.")
            };
        }

        public ErrorKind Kind { get; }
    }
}