using Devolutions.Now.Policy.Api;

namespace Devolutions.Now.Policy.Client;

/// <summary>Client-facing package operation request. Broker-controlled wire metadata is filled by <see cref="BrokerClient"/>.</summary>
public sealed class PackageOperationRequest
{
    /// <summary>Optional stable request identifier. When omitted, <see cref="BrokerClient"/> generates one.</summary>
    public string? RequestId { get; init; }

    /// <summary>Optional request creation timestamp. When omitted, <see cref="BrokerClient"/> uses the current UTC time.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    public required Operation Operation { get; init; }

    public required ManagerName Manager { get; init; }

    public required RequestSource Source { get; init; }

    public required RequestPackage Package { get; init; }

    public RequestOptions Options { get; init; } = new();

    public bool IncludeCommandPreview { get; init; }

    public bool CaptureOutput { get; init; }
}