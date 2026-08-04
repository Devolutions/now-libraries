namespace Devolutions.Now.Policy.Client;

/// <summary>Client-facing operation cancel request. Client context is filled by <see cref="BrokerClient"/>.</summary>
public sealed class OperationCancelQuery
{
    public required string OperationId { get; init; }
}