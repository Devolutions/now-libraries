using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Api;

/// <summary>Canonical response returned by the broker after evaluating a request.</summary>
public sealed class EvaluationResponse
{
    private const string Kind = BrokerApi.EvaluationResponseKind;
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

    [JsonPropertyName("RequestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("ReceivedAt")]
    public DateTimeOffset ReceivedAt { get; set; }

    [JsonPropertyName("CompletedAt")]
    public DateTimeOffset CompletedAt { get; set; }

    [JsonPropertyName("Request")]
    public RequestSummary Request { get; set; } = new();

    [JsonPropertyName("Decision")]
    public DecisionInfo Decision { get; set; } = new();

    [JsonPropertyName("WouldExecute")]
    public bool WouldExecute { get; set; }

    [JsonPropertyName("Policy")]
    public ResponsePolicyInfo Policy { get; set; } = new();

    [JsonPropertyName("Diagnostics")]
    public OperationDiagnostics? Diagnostics { get; set; }
}

/// <summary>Response returned after an execute request is evaluated and submitted when allowed.</summary>
public sealed class ExecutionResponse
{
    private const string Kind = BrokerApi.ExecutionResponseKind;
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

    [JsonPropertyName("RequestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("ReceivedAt")]
    public DateTimeOffset ReceivedAt { get; set; }

    [JsonPropertyName("CompletedAt")]
    public DateTimeOffset CompletedAt { get; set; }

    [JsonPropertyName("Request")]
    public RequestSummary Request { get; set; } = new();

    [JsonPropertyName("Decision")]
    public DecisionInfo Decision { get; set; } = new();

    [JsonPropertyName("Policy")]
    public ResponsePolicyInfo Policy { get; set; } = new();

    [JsonPropertyName("Operation")]
    public OperationSubmission? Operation { get; set; }

    [JsonPropertyName("Diagnostics")]
    public OperationDiagnostics? Diagnostics { get; set; }
}

public sealed class ServerContext
{
    [JsonPropertyName("ServerVersion")]
    public string ServerVersion { get; set; } = "";

    [JsonPropertyName("Transport")]
    public Transport Transport { get; set; }
}

public sealed class RequestSummary
{
    [JsonPropertyName("Manager")]
    public ManagerName? Manager { get; set; }

    [JsonPropertyName("Source")]
    public string? Source { get; set; }

    [JsonPropertyName("PackageId")]
    public string? PackageId { get; set; }

    [JsonPropertyName("Operation")]
    public Operation? Operation { get; set; }
}

public sealed class DecisionInfo
{
    [JsonPropertyName("Decision")]
    public Decision Decision { get; set; }

    [JsonPropertyName("RuleId")]
    public string RuleId { get; set; } = "";

    [JsonPropertyName("Reason")]
    public string Reason { get; set; } = "";
}

public sealed class ResponsePolicyInfo
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Revision")]
    public int Revision { get; set; }

    [JsonPropertyName("PolicyVersion")]
    public string PolicyVersion { get; set; } = "1.0.0";
}

public sealed class OperationDiagnostics
{
    [JsonPropertyName("CommandPreview")]
    public List<string> CommandPreview { get; set; } = [];

}

public sealed class OperationSubmission
{
    [JsonPropertyName("OperationId")]
    public string OperationId { get; set; } = "";

    [JsonPropertyName("Status")]
    public OperationStatus Status { get; set; }

    [JsonPropertyName("SubmittedAt")]
    public DateTimeOffset SubmittedAt { get; set; }
}