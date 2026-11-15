using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Api;

/// <summary>
/// Response returned after a remotely granted package operation is submitted.
/// It identifies <see cref="GrantAuthorizationKind.RemoteGrant"/> as the authorization and carries no
/// policy decision, rule, diagnostics, grant value or captured output.
/// </summary>
public sealed class GrantExecutionResponse
{
    private string _responseKind = BrokerApi.GrantExecutionResponseKind;
    private string _responseVersion = BrokerApi.Version;

    [JsonPropertyName("ResponseKind")]
    [JsonRequired]
    public string ResponseKind
    {
        get => _responseKind;
        set => _responseKind = BrokerApi.ValidateMessageKind(
            value,
            BrokerApi.GrantExecutionResponseKind,
            nameof(ResponseKind));
    }

    /// <summary>Exactly <see cref="BrokerApi.Version"/>.</summary>
    [JsonPropertyName("ResponseVersion")]
    [JsonRequired]
    public string ResponseVersion
    {
        get => _responseVersion;
        set => _responseVersion = BrokerApi.ValidateMessageKind(value, BrokerApi.Version, nameof(ResponseVersion));
    }

    [JsonPropertyName("Server")]
    [JsonRequired]
    public ServerContext Server { get; set; } = new();

    /// <summary>The submitted Broker request ID, a lowercase hyphenated UUID.</summary>
    [JsonPropertyName("RequestId")]
    [JsonRequired]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("ReceivedAt")]
    [JsonRequired]
    public DateTimeOffset ReceivedAt { get; set; }

    [JsonPropertyName("CompletedAt")]
    [JsonRequired]
    public DateTimeOffset CompletedAt { get; set; }

    [JsonPropertyName("Request")]
    [JsonRequired]
    public RequestSummary Request { get; set; } = new();

    [JsonPropertyName("Authorization")]
    [JsonRequired]
    public GrantAuthorization Authorization { get; set; } = new();

    [JsonPropertyName("Operation")]
    [JsonRequired]
    public OperationSubmission Operation { get; set; } = new();
}

/// <summary>Authorization under which a grant-executed operation ran.</summary>
public sealed class GrantAuthorization
{
    [JsonPropertyName("Kind")]
    [JsonRequired]
    public GrantAuthorizationKind Kind { get; set; } = GrantAuthorizationKind.RemoteGrant;
}

/// <summary>Basis on which the broker executed an operation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GrantAuthorizationKind>))]
public enum GrantAuthorizationKind
{
    /// <summary>The operation was authorized by a remote grant, not by local policy.</summary>
    RemoteGrant,
}

/// <summary>
/// Checks a grant response document against the wire contract: required and nullable members, exact
/// discriminators and enumerations, UTC timestamps, Unicode-character lengths, and unknown members at
/// every object level. Duplicate members are rejected by the caller before this runs.
/// </summary>
internal static class GrantResponseValidator
{
    private static readonly string[] s_statuses = ["Starting", "Running", "Canceling", "Completed", "Failed", "Canceled"];
    private static readonly string[] s_transports = ["HttpNamedPipe", "HttpLoopbackSimulator"];
    private static readonly string[] s_operations = ["Install", "Update", "Uninstall"];

    internal static void Validate(string json)
    {
        using var document = JsonDocument.Parse(json);
        try
        {
            ValidateResponse(document.RootElement);
        }
        catch (InvalidOperationException exception)
        {
            throw new JsonException("The grant response contains an invalid string value.", exception);
        }
    }

    private static void ValidateResponse(JsonElement root)
    {
        var members = Members(
            root,
            "$",
            ["ResponseKind", "ResponseVersion", "Server", "RequestId", "ReceivedAt", "CompletedAt", "Request", "Authorization", "Operation"],
            []);

        Exact(members["ResponseKind"], "$.ResponseKind", [BrokerApi.GrantExecutionResponseKind]);
        Exact(members["ResponseVersion"], "$.ResponseVersion", [BrokerApi.Version]);

        var server = Members(members["Server"], "$.Server", ["ServerVersion", "Transport"], []);
        Length(server["ServerVersion"], "$.Server.ServerVersion", 1, 128);
        Exact(server["Transport"], "$.Server.Transport", s_transports);

        RequestId(members["RequestId"]);
        Timestamp(members["ReceivedAt"], "$.ReceivedAt");
        Timestamp(members["CompletedAt"], "$.CompletedAt");

        var request = Members(members["Request"], "$.Request", [], ["Manager", "Source", "PackageId", "Operation"]);
        if (request.TryGetValue("Manager", out var manager) && manager.ValueKind != JsonValueKind.Null)
        {
            Exact(manager, "$.Request.Manager", Enum.GetNames<ManagerName>());
        }
        foreach (var name in (ReadOnlySpan<string>)["Source", "PackageId"])
        {
            if (request.TryGetValue(name, out var text) && text.ValueKind != JsonValueKind.Null)
            {
                Length(text, $"$.Request.{name}", 0, int.MaxValue);
            }
        }
        if (request.TryGetValue("Operation", out var operation) && operation.ValueKind != JsonValueKind.Null)
        {
            Exact(operation, "$.Request.Operation", s_operations);
        }

        var authorization = Members(members["Authorization"], "$.Authorization", ["Kind"], []);
        Exact(authorization["Kind"], "$.Authorization.Kind", [nameof(GrantAuthorizationKind.RemoteGrant)]);

        var submission = Members(
            members["Operation"],
            "$.Operation",
            ["OperationId", "Status", "SubmittedAt"],
            ["EventChannel"]);
        Length(submission["OperationId"], "$.Operation.OperationId", 1, int.MaxValue);
        Exact(submission["Status"], "$.Operation.Status", s_statuses);
        Timestamp(submission["SubmittedAt"], "$.Operation.SubmittedAt");
        if (submission.TryGetValue("EventChannel", out var channel) && channel.ValueKind != JsonValueKind.Null)
        {
            var channelMembers = Members(channel, "$.Operation.EventChannel", ["Kind", "Path"], []);
            Exact(channelMembers["Kind"], "$.Operation.EventChannel.Kind", [nameof(EventChannelKind.LocalPipe)]);
            Length(channelMembers["Path"], "$.Operation.EventChannel.Path", 1, 1024);
        }
    }

    private static Dictionary<string, JsonElement> Members(
        JsonElement element,
        string path,
        string[] required,
        string[] optional)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"{path} must be a JSON object.");
        }

        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!required.Contains(property.Name, StringComparer.Ordinal)
                && !optional.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new JsonException($"{path} contains the unknown member '{property.Name}'.");
            }

            members[property.Name] = property.Value;
        }

        foreach (var name in required)
        {
            if (!members.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                throw new JsonException($"{path}.{name} is required and must not be null.");
            }
        }

        return members;
    }

    private static string Text(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? throw new JsonException($"{path} must be a string.")
            : throw new JsonException($"{path} must be a string.");

    private static void Exact(JsonElement element, string path, IReadOnlyCollection<string> allowed)
    {
        if (!allowed.Contains(Text(element, path), StringComparer.Ordinal))
        {
            throw new JsonException($"{path} is not an allowed value.");
        }
    }

    private static void Length(JsonElement element, string path, int min, int max)
    {
        var length = Text(element, path).EnumerateRunes().Count();
        if (length < min || length > max)
        {
            throw new JsonException($"{path} must be {min} to {max} characters.");
        }
    }

    private static void Timestamp(JsonElement element, string path)
    {
        var text = Text(element, path);
        if (text.Length < 11 || text[10] != 'T' || !(text.EndsWith('Z') || text.EndsWith("+00:00", StringComparison.Ordinal)) || !element.TryGetDateTimeOffset(out _))
        {
            throw new JsonException($"{path} must be an RFC 3339 UTC timestamp.");
        }
    }

    private static void RequestId(JsonElement element)
    {
        var text = Text(element, "$.RequestId");
        if (!Guid.TryParseExact(text, "D", out var guid) || text != guid.ToString("D"))
        {
            throw new JsonException("$.RequestId must be a lowercase hyphenated UUID.");
        }
    }
}