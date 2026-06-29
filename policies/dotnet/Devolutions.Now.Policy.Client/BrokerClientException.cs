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
        : base(message, innerException)
    {
        Kind = kind;
        Endpoint = endpoint;
        StatusCode = statusCode;
        BrokerError = brokerError;
    }

    public BrokerClientErrorKind Kind { get; }

    public string? Endpoint { get; }

    public int? StatusCode { get; }

    public ErrorResponse? BrokerError { get; }
}