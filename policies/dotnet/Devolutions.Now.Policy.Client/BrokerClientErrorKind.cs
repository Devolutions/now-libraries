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

    /// <summary>The named pipe server could not be verified as the package broker; no request data was sent.</summary>
    ServerVerificationFailed,
}