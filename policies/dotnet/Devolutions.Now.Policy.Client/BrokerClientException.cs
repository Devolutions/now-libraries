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
        Exception? innerException = null,
        bool noWorkStarted = false,
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
    /// <c>true</c> only when the request was never sent, or the broker returned a contract rejection
    /// that starts no work for it. <c>false</c> means the outcome may be unknown: the request may
    /// have been sent and work may have started.
    /// </summary>
    public bool NoWorkStarted { get; }

    /// <summary>Code of a validated broker rejection, when one was received.</summary>
    public ErrorCode? BrokerErrorCode { get; }
}