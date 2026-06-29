using Devolutions.Now.Policy.Api;

namespace Devolutions.Now.Policy.Client;

/// <summary>Abstraction for package broker request/response transports.</summary>
public interface IBrokerTransport : IDisposable
{
    /// <summary>API transport kind represented in client context and broker capabilities.</summary>
    Transport Kind { get; }

    /// <summary>Send one broker HTTP-style request and return the response.</summary>
    Task<BrokerTransportResponse> Send(BrokerTransportRequest request, CancellationToken cancellationToken = default);
}