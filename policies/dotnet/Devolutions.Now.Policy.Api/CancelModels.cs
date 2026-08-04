using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Api;

/// <summary>Request body for canceling a previously submitted operation.</summary>
public sealed class CancelRequest
{
    private const string Kind = BrokerApi.CancelRequestKind;
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

/// <summary>Response to a cancel request.</summary>
/// <remarks>
/// Cancelation is asynchronous and idempotent: the broker acknowledges the request by moving a
/// non-terminal operation to <see cref="OperationStatus.Canceling"/> and reports the resulting
/// status. Clients should poll the status endpoint until the operation reaches a terminal status
/// (<see cref="OperationStatus.Canceled"/>, or <see cref="OperationStatus.Completed"/> /
/// <see cref="OperationStatus.Failed"/> when the process ends first).
/// </remarks>
public sealed class CancelResponse
{
    private const string Kind = BrokerApi.CancelResponseKind;
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

    /// <summary>Status of the operation after the cancel request was applied.</summary>
    [JsonPropertyName("Status")]
    public OperationStatus Status { get; set; }

    /// <summary>Human-readable message about the cancelation outcome.</summary>
    [JsonPropertyName("Message")]
    public string? Message { get; set; }
}