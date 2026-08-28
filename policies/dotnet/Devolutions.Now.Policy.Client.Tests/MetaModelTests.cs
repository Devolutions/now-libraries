using System.Text.Json;
using System.Text.Json.Nodes;

using Devolutions.Now.Policy.Model;

using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

/// <summary>
/// Sync guard for DTOs without canonical sample files. Health and capabilities are covered by
/// shared Rust samples in the round-trip tests.
/// </summary>
public class MetaModelTests
{
    [Fact]
    public void RequestKind_rejects_wrong_value_on_deserialization()
    {
        const string json = """{"RequestKind":"StatusRequest","RequestVersion":"1.0"}""";

        Assert.Throws<JsonException>(() => BrokerJson.DeserializeStrict<PackageRequest>(json));
    }

    [Fact]
    public void RequestKind_is_required_on_deserialization()
    {
        const string json = """{"RequestVersion":"1.0"}""";

        Assert.Throws<JsonException>(() => BrokerJson.DeserializeStrict<PackageRequest>(json));
    }

    [Fact]
    public void ResponseKind_rejects_wrong_value_on_deserialization()
    {
        const string json =
            """
            {"ResponseKind":"ErrorResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"Status":"Ready","PolicyId":"mock.policy"}
            """;

        Assert.Throws<JsonException>(() => BrokerJson.DeserializeStrict<HealthResponse>(json));
    }

    [Fact]
    public void PolicyResponseKind_rejects_wrong_value_on_deserialization()
    {
        var json = File.ReadAllText(Path.Combine(TestData.SamplesDir, "responses", "policy.response.json"))
            .Replace(BrokerApi.PolicyResponseKind, BrokerApi.ErrorResponseKind, StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => BrokerJson.DeserializeStrict<PolicyResponse>(json));
    }

    [Fact]
    public void PolicyResponse_requires_policy_on_deserialization()
    {
        const string json =
            """
            {"ResponseKind":"PolicyResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"}}
            """;

        Assert.Throws<JsonException>(() => BrokerJson.Deserialize<PolicyResponse>(json));
    }

    [Theory]
    [InlineData("ResponseVersion")]
    [InlineData("Server")]
    [InlineData("Server.ServerVersion")]
    [InlineData("Policy")]
    [InlineData("Policy.Metadata")]
    [InlineData("Policy.Metadata.Id")]
    [InlineData("Policy.Enforcement.DefaultDecision")]
    [InlineData("Policy.Rules")]
    [InlineData("Policy.Rules.0.Match")]
    [InlineData("Policy.Rules.0.Match.Operations")]
    public void PolicyResponse_rejects_null_non_nullable_property(string propertyPath)
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var document = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException("policy response sample should parse");
        SetPropertyToNull(document, propertyPath);
        var json = document.ToJsonString();

        Assert.Throws<JsonException>(() => BrokerJson.Deserialize<PolicyResponse>(json));
        Assert.Throws<JsonException>(() => BrokerJson.DeserializeStrict<PolicyResponse>(json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PolicyResponse>(json, BrokerJson.Options));
    }

    [Theory]
    [InlineData("Policy.Rules.0")]
    [InlineData("Policy.Rules.3.Match.Sources.0")]
    public void Strict_policy_response_rejects_null_collection_element(string elementPath)
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var document = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException("policy response sample should parse");
        SetPropertyToNull(document, elementPath);

        Assert.Throws<JsonException>(() => BrokerJson.DeserializeStrict<PolicyResponse>(document.ToJsonString()));
    }

    [Fact]
    public void Public_json_options_source_generate_all_broker_dtos()
    {
        Type[] dtoTypes =
        [
            typeof(PackageRequest),
            typeof(RequestSource),
            typeof(RequestPackage),
            typeof(RequestOptions),
            typeof(ClientContext),
            typeof(PolicyResponse),
            typeof(PolicyManagementResponse),
            typeof(PolicyManagementSnapshot),
            typeof(InvalidPolicyDiagnostics),
            typeof(PolicyValidationRequest),
            typeof(PolicyValidationResponse),
            typeof(PolicyValidationResult),
            typeof(PolicyFinding),
            typeof(PolicyReplacementRequest),
            typeof(PolicyReplacementResponse),
            typeof(EvaluationResponse),
            typeof(ExecutionResponse),
            typeof(ServerContext),
            typeof(RequestSummary),
            typeof(DecisionInfo),
            typeof(ResponsePolicyInfo),
            typeof(OperationDiagnostics),
            typeof(OperationSubmission),
            typeof(StatusRequest),
            typeof(StatusResponse),
            typeof(CancelRequest),
            typeof(CancelResponse),
            typeof(HealthResponse),
            typeof(CapabilitiesResponse),
            typeof(ManagerCapability),
            typeof(ErrorResponse),
            typeof(ErrorDetail),
            typeof(EventChannel),
            typeof(PolicyDocument),
            typeof(PolicyDraftDocument),
            typeof(PolicyMetadata),
            typeof(PolicyDraftMetadata),
            typeof(PolicyEnforcement),
            typeof(PolicyRule),
            typeof(PolicyMatch),
            typeof(VersionRange),
            typeof(PolicyConstraints),
        ];

        foreach (var dtoType in dtoTypes)
        {
            Assert.NotNull(BrokerJson.Options.GetTypeInfo(dtoType));
            Assert.NotNull(BrokerJson.PrettyOptions.GetTypeInfo(dtoType));
        }
    }

    [Fact]
    public void Public_json_options_round_trip_policy_response_without_reflection()
    {
        var json = File.ReadAllText(Path.Combine(TestData.SamplesDir, "responses", "policy.response.json"));
        var response = JsonSerializer.Deserialize<PolicyResponse>(json, BrokerJson.Options);

        Assert.NotNull(response);

        var compact = JsonSerializer.Serialize(response, BrokerJson.Options);
        var pretty = JsonSerializer.Serialize(response, BrokerJson.PrettyOptions);

        Assert.NotNull(JsonSerializer.Deserialize<PolicyResponse>(compact, BrokerJson.Options));
        Assert.NotNull(JsonSerializer.Deserialize<PolicyResponse>(pretty, BrokerJson.PrettyOptions));
        Assert.Contains(Environment.NewLine, pretty);
    }

    [Fact]
    public async Task ErrorResponse_serializes_to_schema_valid_output()
    {
        var full = new ErrorResponse
        {
            Server = CreateServerContext(),
            Code = ErrorCode.BrokerPaused,
            Message = "policy file is unavailable or corrupted; waiting for a valid policy",
            Details =
            [
                new ErrorDetail
                {
                    Code = "PolicyUnavailable",
                    Path = "Policy",
                    Message = "No valid policy is active.",
                },
            ],
        };
        await AssertSerializesValid(full, "ErrorResponse");

        // Optional fields omitted when null (mirrors the Rust skip_serializing_if).
        var minimal = new ErrorResponse
        {
            Server = CreateServerContext(),
            Code = ErrorCode.BadRequest,
            Message = "request body is required",
        };
        await AssertSerializesValid(minimal, "ErrorResponse");
    }

    private static ServerContext CreateServerContext() => new()
    {
        ServerVersion = "0.1.0",
        Transport = Transport.HttpNamedPipe,
    };

    private static void SetPropertyToNull(JsonNode document, string propertyPath)
    {
        var segments = propertyPath.Split('.');
        var parent = document;
        foreach (var segment in segments[..^1])
        {
            parent = int.TryParse(segment, out var index)
                ? parent.AsArray()[index]!
                : parent[segment]!;
        }

        if (int.TryParse(segments[^1], out var finalIndex))
        {
            Assert.NotNull(parent.AsArray()[finalIndex]);
            parent.AsArray()[finalIndex] = null;
        }
        else
        {
            Assert.NotNull(parent.AsObject()[segments[^1]]);
            parent.AsObject()[segments[^1]] = null;
        }
    }

    private static async Task AssertSerializesValid<T>(T dto, string componentName)
    {
        var schema = await TestData.SchemaAsync(componentName);
        var json = BrokerJson.Serialize(dto);

        // Output must satisfy the schema (catches missing required fields / type drift).
        var errors = schema.Validate(json);
        Assert.True(
            errors.Count == 0,
            $"Serialized {typeof(T).Name} failed {componentName} schema validation:\n" +
            string.Join("\n", errors.Select(e => $"  {e.Kind} at {e.Path}")));

        // Round-trip back through the DTO with strict mapping (catches schema fields the DTO drops).
        var reparsed = BrokerJson.DeserializeStrict<T>(json);
        Assert.NotNull(reparsed);
    }
}