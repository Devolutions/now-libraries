namespace Devolutions.Now.Policy.Client;

/// <summary>A transport response together with the server identity that was authenticated before the request was written.</summary>
public sealed class AuthenticatedBrokerTransportResponse
{
    public required BrokerTransportResponse Response { get; init; }

    public required BrokerPipeServerIdentity Server { get; init; }
}

/// <summary>A transport that can authenticate the server of the connection that carries one request.</summary>
public interface IAuthenticatedBrokerTransport : IBrokerTransport
{
    /// <summary>
    /// Connect, call <paramref name="authenticator"/> with the server identity, and only then write
    /// <paramref name="request"/>. A rejected server throws <see cref="BrokerClientException"/> with
    /// <see cref="BrokerClientErrorKind.ServerAuthenticationFailed"/> and nothing is written.
    /// </summary>
    Task<AuthenticatedBrokerTransportResponse> SendAuthenticated(
        BrokerTransportRequest request,
        IBrokerServerAuthenticator authenticator,
        CancellationToken cancellationToken = default);
}