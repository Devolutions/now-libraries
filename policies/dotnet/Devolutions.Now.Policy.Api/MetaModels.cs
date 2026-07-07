using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Api;

/// <summary>Response body for <c>GET /v1/health</c>.</summary>
public sealed class HealthResponse
{
    private const string Kind = BrokerApi.HealthResponseKind;
    private string _responseKind = Kind;

    [JsonPropertyName("ResponseKind")]
    [JsonRequired]
    public string ResponseKind
    {
        get => _responseKind;
        set => _responseKind = BrokerApi.ValidateMessageKind(value, Kind, nameof(ResponseKind));
    }

    [JsonPropertyName("ResponseVersion")]
    public string ResponseVersion { get; set; } = BrokerApi.Version;

    [JsonPropertyName("Server")]
    public ServerContext Server { get; set; } = new();

    [JsonPropertyName("Status")]
    public HealthStatus Status { get; set; }

    [JsonPropertyName("PolicyId")]
    public string PolicyId { get; set; } = "";

}

/// <summary>Response body for <c>GET /v1/capabilities</c>.</summary>
public sealed class CapabilitiesResponse
{
    private const string Kind = BrokerApi.CapabilitiesResponseKind;
    private string _responseKind = Kind;

    [JsonPropertyName("ResponseKind")]
    [JsonRequired]
    public string ResponseKind
    {
        get => _responseKind;
        set => _responseKind = BrokerApi.ValidateMessageKind(value, Kind, nameof(ResponseKind));
    }

    [JsonPropertyName("ResponseVersion")]
    public string ResponseVersion { get; set; } = BrokerApi.Version;

    [JsonPropertyName("Server")]
    public ServerContext Server { get; set; } = new();

    [JsonPropertyName("Transports")]
    public List<Transport> Transports { get; set; } = [];

    [JsonPropertyName("Managers")]
    public List<ManagerCapability> Managers { get; set; } = [];

    [JsonPropertyName("MaxRequestBodyBytes")]
    public long MaxRequestBodyBytes { get; set; }
}

public sealed class ManagerCapability
{
    [JsonPropertyName("Manager")]
    public ManagerName Manager { get; set; }

    [JsonPropertyName("Operations")]
    public List<Operation> Operations { get; set; } = [];

    [JsonPropertyName("Scopes")]
    public List<Scope> Scopes { get; set; } = [];

    [JsonPropertyName("Architectures")]
    public List<Architecture> Architectures { get; set; } = [];

    [JsonPropertyName("SupportsCustomParameters")]
    public bool SupportsCustomParameters { get; set; }

    [JsonPropertyName("SupportsCustomInstallLocation")]
    public bool SupportsCustomInstallLocation { get; set; }

    [JsonPropertyName("SupportsCaptureOutput")]
    public bool SupportsCaptureOutput { get; set; }

    [JsonPropertyName("SupportsDetails")]
    public bool SupportsDetails { get; set; }

    [JsonPropertyName("MaxOperationTimeoutSeconds")]
    public ulong? MaxOperationTimeoutSeconds { get; set; }
}

/// <summary>Generic error body returned for non-2xx responses.</summary>
public sealed class ErrorResponse
{
    private const string Kind = BrokerApi.ErrorResponseKind;
    private string _responseKind = Kind;

    [JsonPropertyName("ResponseKind")]
    [JsonRequired]
    public string ResponseKind
    {
        get => _responseKind;
        set => _responseKind = BrokerApi.ValidateMessageKind(value, Kind, nameof(ResponseKind));
    }

    [JsonPropertyName("ResponseVersion")]
    public string ResponseVersion { get; set; } = BrokerApi.Version;

    [JsonPropertyName("Server")]
    public ServerContext Server { get; set; } = new();

    [JsonPropertyName("Code")]
    public ErrorCode Code { get; set; }

    [JsonPropertyName("Message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("Details")]
    public List<ErrorDetail> Details { get; set; } = [];

}

public sealed class ErrorDetail
{
    [JsonPropertyName("Code")]
    public string? Code { get; set; }

    [JsonPropertyName("Path")]
    public string? Path { get; set; }

    [JsonPropertyName("Message")]
    public string Message { get; set; } = "";
}