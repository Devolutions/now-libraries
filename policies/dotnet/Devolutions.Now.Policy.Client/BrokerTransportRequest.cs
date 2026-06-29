namespace Devolutions.Now.Policy.Client;

/// <summary>HTTP-style request passed from <see cref="BrokerClient"/> to an underlying broker transport.</summary>
public sealed class BrokerTransportRequest
{
    public required string Method { get; init; }

    public required string Path { get; init; }

    public string? Body { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}