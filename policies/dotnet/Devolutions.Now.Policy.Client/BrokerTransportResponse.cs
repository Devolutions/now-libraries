namespace Devolutions.Now.Policy.Client;

/// <summary>HTTP-style response returned by an underlying broker transport.</summary>
public sealed class BrokerTransportResponse
{
    public required int StatusCode { get; init; }

    public required string Body { get; init; }
}