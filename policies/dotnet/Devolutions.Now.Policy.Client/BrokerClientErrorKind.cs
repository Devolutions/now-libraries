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

    /// <summary>
    /// The broker's protocol version predates the requested feature (e.g. a
    /// package manager introduced in a newer API version). Distinct from
    /// <see cref="UnsupportedCapability"/>, which means the broker understands
    /// the feature but does not advertise support for it.
    /// </summary>
    UnsupportedApiVersion,
    RequestTooLarge,
}