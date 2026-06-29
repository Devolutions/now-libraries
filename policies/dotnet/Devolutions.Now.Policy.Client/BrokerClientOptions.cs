using Devolutions.Now.Policy.Api;

namespace Devolutions.Now.Policy.Client;

/// <summary>Configuration used to populate package broker client context fields.</summary>
public sealed class BrokerClientOptions
{
    /// <summary>Windows identity of the effective user for package broker requests. Defaults to the current user.</summary>
    public string? EffectiveUser { get; init; }

    /// <summary>Elevation level requested by the client.</summary>
    public required Elevation RequestedElevation { get; init; }

    /// <summary>
    /// Transport used to reach the package broker. Defaults to <see cref="NamedPipeBrokerTransport"/>
    /// using <see cref="BrokerApi.DefaultPipeName"/>.
    /// </summary>
    public IBrokerTransport? Transport { get; init; }

    /// <summary>Named pipe exposed by the package broker when <see cref="Transport"/> is not specified.</summary>
    public string? PipeName { get; init; }

    /// <summary>Path to the client executable authenticated by the broker. Defaults to the current process path.</summary>
    public string? ClientExecutablePath { get; init; }

    /// <summary>Version of the client binary. Defaults to the client assembly version.</summary>
    public string? ClientVersion { get; init; }
}