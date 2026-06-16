namespace Devolutions.NowProto.Exceptions
{
    /// <summary>
    /// Base class for all exceptions thrown by the NowProto library.
    /// </summary>
    public class NowException : Exception
    {
        internal NowException(string displayMessage)
            : base(displayMessage)
        {
        }
    }
}