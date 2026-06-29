namespace Devolutions.Now.Policy.Client;

/// <summary>Client-facing operation status query. Client context is filled by <see cref="BrokerClient"/>.</summary>
public sealed class OperationStatusQuery
{
    public required string OperationId { get; init; }
}