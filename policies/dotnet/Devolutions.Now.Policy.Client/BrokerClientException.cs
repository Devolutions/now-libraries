using Devolutions.Now.Policy.Api;

namespace Devolutions.Now.Policy.Client;

/// <summary>Exception raised when the package broker client cannot complete a broker request.</summary>
public sealed class BrokerClientException : Exception
{
    public BrokerClientException(
        BrokerClientErrorKind kind,
        string message,
        string? endpoint = null,
        int? statusCode = null,
        ErrorResponse? brokerError = null,
        Exception? innerException = null)
        : this(kind, message, false, endpoint, statusCode, brokerError, innerException)
    {
    }

    internal BrokerClientException(
        BrokerClientErrorKind kind,
        string message,
        bool noWorkStarted,
        string? endpoint = null,
        int? statusCode = null,
        ErrorResponse? brokerError = null,
        Exception? innerException = null,
        ErrorCode? brokerErrorCode = null)
        : base(message, innerException)
    {
        Kind = kind;
        Endpoint = endpoint;
        StatusCode = statusCode;
        BrokerError = brokerError;
        NoWorkStarted = noWorkStarted;
        BrokerErrorCode = brokerErrorCode;
    }

    public BrokerClientErrorKind Kind { get; }

    public string? Endpoint { get; }

    public int? StatusCode { get; }

    public ErrorResponse? BrokerError { get; }

    /// <summary>
    /// <c>true</c> only when this request was never sent or was rejected before starting work.
    /// An earlier or concurrent request may still have started work.
    /// <c>false</c> means this request may have been sent and may have started work.
    /// </summary>
    public bool NoWorkStarted { get; }

    /// <summary>Code of a validated broker rejection, when one was received.</summary>
    public ErrorCode? BrokerErrorCode { get; }
}