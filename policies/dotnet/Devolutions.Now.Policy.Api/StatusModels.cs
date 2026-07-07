using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Api;

/// <summary>Request body for querying an operation status.</summary>
public sealed class StatusRequest
{
    private const string Kind = BrokerApi.StatusRequestKind;
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

    [JsonPropertyName("OperationId")]
    public string OperationId { get; set; } = "";

    [JsonPropertyName("Client")]
    public ClientContext Client { get; set; } = new();
}

/// <summary>Response to a status query.</summary>
public sealed class StatusResponse
{
    private const string Kind = BrokerApi.StatusResponseKind;
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

    [JsonPropertyName("OperationId")]
    public string OperationId { get; set; } = "";

    [JsonPropertyName("RequestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("Status")]
    public OperationStatus Status { get; set; }

    [JsonPropertyName("StartedAt")]
    public DateTimeOffset? StartedAt { get; set; }

    [JsonPropertyName("CompletedAt")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("ExitCode")]
    public int? ExitCode { get; set; }

    [JsonPropertyName("Message")]
    public string? Message { get; set; }

    [JsonPropertyName("Details")]
    public JsonNode? Details { get; set; }

    /// <summary>
    /// Captured combined stdout+stderr of the operation as base64-encoded UTF-8 data (tail-truncated to ~10 KiB before encoding).
    /// Only present when the request opted in via <see cref="PackageRequest.CaptureOutput"/>.
    /// </summary>
    [JsonPropertyName("Stdout")]
    public string? Stdout { get; set; }
}