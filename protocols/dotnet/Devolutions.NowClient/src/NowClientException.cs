namespace Devolutions.NowClient
{
    /// <summary>
    /// Base class for all exceptions thrown by the NowClient library.
    /// </summary>
    public class NowClientException : Exception
    {
        internal NowClientException(string displayMessage)
            : base(displayMessage)
        {
        }
    }
}