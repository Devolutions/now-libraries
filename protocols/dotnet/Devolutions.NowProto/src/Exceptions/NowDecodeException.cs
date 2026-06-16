using System.Diagnostics;

namespace Devolutions.NowProto.Exceptions
{
    /// <summary>
    /// NOW-proto decoding exception.
    /// </summary>
    public class NowDecodeException : NowException
    {
        internal NowDecodeException(ErrorKind kind)
            : base(KindToMessage(kind))
        {
            Kind = kind;
        }

        public enum ErrorKind
        {
            NotEnoughBytes,
            UnexpectedMessageClass,
            UnexpectedMessageKind,
            InvalidDataStreamFlags,
            InvalidApartmentStateFlags,
            InvalidKbdLayoutFlags,
            InvalidWindowRecEventFlags,
        }

        private static string KindToMessage(ErrorKind kind)
        {
            return kind switch
            {
                ErrorKind.NotEnoughBytes => "Not enough bytes to decode the value.",
                ErrorKind.UnexpectedMessageClass => "Unexpected message class.",
                ErrorKind.UnexpectedMessageKind => "Unexpected message kind.",
                ErrorKind.InvalidDataStreamFlags => "Invalid data stream flags.",
                ErrorKind.InvalidApartmentStateFlags => "Invalid apartment state flags.",
                ErrorKind.InvalidKbdLayoutFlags => "Invalid keyboard layout flags.",
                ErrorKind.InvalidWindowRecEventFlags => "Invalid window recording event flags.",
                _ => throw new UnreachableException("Should not be constructed with invalid kind."),
            };
        }

        public ErrorKind Kind { get; }
    }
}