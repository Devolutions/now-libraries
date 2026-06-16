namespace Devolutions.NowClient;

/// <summary>
/// Exception thrown by NowRdmHost when RDM operations fail.
/// </summary>
public class NowRdmHostException : Exception
{
    public NowRdmHostException(string message) : base(message)
    {
    }

    public NowRdmHostException(string message, Exception innerException) : base(message, innerException)
    {
    }
}