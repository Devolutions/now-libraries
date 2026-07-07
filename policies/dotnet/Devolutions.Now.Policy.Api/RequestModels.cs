using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Api;

/// <summary>Canonical request sent by a package broker client to the elevated broker.</summary>
public sealed class PackageRequest
{
    private const string Kind = BrokerApi.PackageRequestKind;
    private string _requestKind = Kind;

    [JsonPropertyName("RequestKind")]
    [JsonRequired]
    public string RequestKind
    {
        get => _requestKind;
        set => _requestKind = BrokerApi.ValidateMessageKind(value, Kind, nameof(RequestKind));
    }

    [JsonPropertyName("RequestVersion")]
    public string RequestVersion { get; set; } = BrokerApi.Version;

    [JsonPropertyName("RequestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("CreatedAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("Operation")]
    public Operation Operation { get; set; }

    [JsonPropertyName("Manager")]
    public ManagerName Manager { get; set; }

    [JsonPropertyName("Source")]
    public RequestSource Source { get; set; } = new();

    [JsonPropertyName("Package")]
    public RequestPackage Package { get; set; } = new();

    [JsonPropertyName("Options")]
    public RequestOptions Options { get; set; } = new();

    [JsonPropertyName("Client")]
    public ClientContext Client { get; set; } = new();

    /// <summary>
    /// When true, evaluation and execution responses may include a command preview for diagnostics.
    /// Off by default because command previews can expose paths or arguments.
    /// </summary>
    [JsonPropertyName("IncludeCommandPreview")]
    public bool IncludeCommandPreview { get; set; }

    /// <summary>
    /// When true, the broker captures the operation's combined stdout+stderr and returns it
    /// (tail-truncated) in the status response. Off by default to avoid the overhead when not needed.
    /// </summary>
    [JsonPropertyName("CaptureOutput")]
    public bool CaptureOutput { get; set; }
}

public sealed class RequestSource
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Url")]
    public string? Url { get; set; }

}

public sealed class RequestPackage
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Version")]
    public string? Version { get; set; }

    [JsonPropertyName("Architecture")]
    public Architecture? Architecture { get; set; }

    [JsonPropertyName("Channel")]
    public string? Channel { get; set; }
}

public sealed class RequestOptions
{
    [JsonPropertyName("Scope")]
    public Scope? Scope { get; set; }

    [JsonPropertyName("Interactive")]
    public bool Interactive { get; set; }

    [JsonPropertyName("SkipHashCheck")]
    public bool SkipHashCheck { get; set; }

    [JsonPropertyName("PreRelease")]
    public bool PreRelease { get; set; }

    [JsonPropertyName("CustomInstallLocation")]
    public string? CustomInstallLocation { get; set; }

    [JsonPropertyName("CustomParameters")]
    public List<string> CustomParameters { get; set; } = [];

    [JsonPropertyName("PreOperationCommand")]
    public string? PreOperationCommand { get; set; }

    [JsonPropertyName("PostOperationCommand")]
    public string? PostOperationCommand { get; set; }

    [JsonPropertyName("KillBeforeOperation")]
    public List<string> KillBeforeOperation { get; set; } = [];

    [JsonPropertyName("UninstallPrevious")]
    public bool UninstallPrevious { get; set; }

    [JsonPropertyName("NoUpgrade")]
    public bool NoUpgrade { get; set; }
}

public sealed class ClientContext
{
    [JsonPropertyName("Transport")]
    public Transport Transport { get; set; }

    [JsonPropertyName("RequestedElevation")]
    public Elevation RequestedElevation { get; set; }

    [JsonPropertyName("EffectiveUser")]
    public string EffectiveUser { get; set; } = "";

    [JsonPropertyName("ClientExecutablePath")]
    public string ClientExecutablePath { get; set; } = "";

    [JsonPropertyName("ClientVersion")]
    public string ClientVersion { get; set; } = "";

}