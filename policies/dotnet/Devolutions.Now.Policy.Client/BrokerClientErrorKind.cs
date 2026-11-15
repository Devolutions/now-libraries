namespace Devolutions.Now.Policy.Client;

/// <summary>Classifies failures reported by <see cref="BrokerClient"/>.</summary>
public enum BrokerClientErrorKind
{
    BrokerUnavailable,
    Timeout,
    EmptyResponse,
    InvalidResponse,
    InvalidRequest,
    BrokerError,
    PolicyDenied,
    UnsupportedCapability,
    RequestTooLarge,

    /// <summary>The pipe server could not be authenticated as the live broker service.</summary>
    ServerAuthenticationFailed,

    /// <summary>The broker does not offer the requested route.</summary>
    UnsupportedEndpoint,
}