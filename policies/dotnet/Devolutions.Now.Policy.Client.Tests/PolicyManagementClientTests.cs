using System.Text.Json;
using System.Text.Json.Nodes;

using Devolutions.Now.Policy.Client;

using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

public class PolicyManagementClientTests
{
    [Fact]
    public async Task GetPolicyManagement_sends_json_get_and_strictly_parses_snapshot()
    {
        var body = await ReadFixture("responses", "policy-management.active.response.json");
        var transport = new FakeBrokerTransport(new BrokerTransportResponse { StatusCode = 200, Body = body });
        var response = await CreateClient(transport).GetPolicyManagement();

        var request = Assert.Single(transport.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/v1/policy/management", request.Path);
        Assert.Equal(PolicyManagementState.Active, response.Management.State);
        Assert.Equal("store:active:7", response.Management.StoreToken);
    }

    [Fact]
    public async Task ValidatePolicy_preserves_raw_unknown_fields_and_returns_exact_warnings()
    {
        var requestJson = await ReadFixture("requests", "policy-validation.request.json");
        using var requestDocument = JsonDocument.Parse(requestJson);
        var body = await ReadFixture("responses", "policy-validation.valid.response.json");
        var transport = new FakeBrokerTransport(new BrokerTransportResponse { StatusCode = 200, Body = body });

        var response = await CreateClient(transport).ValidatePolicy(requestDocument.RootElement.GetProperty("Draft"));

        var request = Assert.Single(transport.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/v1/policy/validate", request.Path);
        using var sent = JsonDocument.Parse(request.Body!);
        Assert.True(sent.RootElement.GetProperty("Draft").GetProperty("EditorExtension").GetProperty("preserved").GetBoolean());
        Assert.Equal("receipt:sha256:valid-warning-set", response.Validation.ValidationReceipt);
        Assert.Equal(3, response.Validation.Findings.Count);
    }

    [Theory]
    [InlineData("policy-replacement.update.request.json", PolicyReplacementOperation.Update, PolicyConflictHandling.Reject)]
    [InlineData("policy-replacement.replace-identity.request.json", PolicyReplacementOperation.ReplaceIdentity, PolicyConflictHandling.Reject)]
    [InlineData("policy-replacement.create.request.json", PolicyReplacementOperation.Create, PolicyConflictHandling.Reject)]
    [InlineData("policy-replacement.repair.request.json", PolicyReplacementOperation.Repair, PolicyConflictHandling.Reject)]
    [InlineData("policy-replacement.overwrite.request.json", PolicyReplacementOperation.Update, PolicyConflictHandling.ConfirmOverwrite)]
    public async Task ReplacePolicy_sends_every_operation_intent(
        string fixture,
        PolicyReplacementOperation operation,
        PolicyConflictHandling conflictHandling)
    {
        var request = BrokerJson.DeserializeStrict<PolicyReplacementRequest>(await ReadFixture("requests", fixture))!;
        var body = await ReadFixture("responses", "policy-replacement.response.json");
        var transport = new FakeBrokerTransport(new BrokerTransportResponse { StatusCode = 200, Body = body });

        var response = await CreateClient(transport).ReplacePolicy(request);

        var sentRequest = Assert.Single(transport.Requests);
        Assert.Equal("PUT", sentRequest.Method);
        Assert.Equal("/v1/policy", sentRequest.Path);
        using var sent = JsonDocument.Parse(sentRequest.Body!);
        Assert.Equal(operation.ToString(), sent.RootElement.GetProperty("Operation").GetString());
        Assert.Equal(conflictHandling.ToString(), sent.RootElement.GetProperty("ConflictHandling").GetString());
        Assert.Equal(8U, response.Policy.Metadata.Revision);
        Assert.Equal("store:active:8", response.Management.StoreToken);
    }

    [Fact]
    public async Task ReplacePolicy_preserves_structured_stale_token_findings()
    {
        var request = BrokerJson.DeserializeStrict<PolicyReplacementRequest>(
            await ReadFixture("requests", "policy-replacement.update.request.json"))!;
        var errorBody = await ReadFixture("responses", "policy-stale-token.error.json");
        var transport = new FakeBrokerTransport(new BrokerTransportResponse { StatusCode = 409, Body = errorBody });

        var exception = await Assert.ThrowsAsync<BrokerClientException>(
            () => CreateClient(transport).ReplacePolicy(request));

        Assert.Equal(ErrorCode.StalePolicyStoreToken, exception.BrokerError?.Code);
        Assert.Equal(PolicyFindingCode.InvalidFieldValue, exception.BrokerError?.Validation?.Findings[0].Code);
    }

    [Theory]
    [InlineData("management")]
    [InlineData("validation")]
    [InlineData("replacement")]
    public async Task Management_success_responses_reject_unknown_members(string operation)
    {
        var (directory, fixture) = operation switch
        {
            "management" => ("responses", "policy-management.active.response.json"),
            "validation" => ("responses", "policy-validation.valid.response.json"),
            _ => ("responses", "policy-replacement.response.json"),
        };
        var document = JsonNode.Parse(await ReadFixture(directory, fixture))!;
        document["Unexpected"] = true;
        var transport = new FakeBrokerTransport(
            new BrokerTransportResponse { StatusCode = 200, Body = document.ToJsonString() });
        var client = CreateClient(transport);

        var exception = operation switch
        {
            "management" => await Assert.ThrowsAsync<BrokerClientException>(() => client.GetPolicyManagement()),
            "validation" => await Assert.ThrowsAsync<BrokerClientException>(
                () => client.ValidatePolicy(JsonDocument.Parse("{}").RootElement)),
            _ => await Assert.ThrowsAsync<BrokerClientException>(
                async () => await client.ReplacePolicy(BrokerJson.DeserializeStrict<PolicyReplacementRequest>(
                    await ReadFixture("requests", "policy-replacement.update.request.json"))!)),
        };

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, exception.Kind);
    }

    [Fact]
    public async Task Management_methods_propagate_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var transport = new FakeBrokerTransport();
        var client = CreateClient(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetPolicyManagement(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ValidatePolicy(JsonDocument.Parse("{}").RootElement, cancellation.Token));
        Assert.Empty(transport.Requests);
    }

    [Theory]
    [InlineData("State", "active")]
    [InlineData("State", 0)]
    public async Task Strict_management_response_rejects_noncanonical_enums(string property, object value)
    {
        var document = JsonNode.Parse(await ReadFixture("responses", "policy-management.active.response.json"))!;
        document["Management"]![property] = value is int number
            ? JsonValue.Create(number)
            : JsonValue.Create((string)value);

        Assert.Throws<JsonException>(
            () => BrokerJson.DeserializeStrict<PolicyManagementResponse>(document.ToJsonString()));
    }

    [Fact]
    public async Task Strict_management_contract_rejects_empty_tokens_and_receipts()
    {
        var management = JsonNode.Parse(await ReadFixture("responses", "policy-management.active.response.json"))!;
        management["Management"]!["StoreToken"] = "";
        Assert.Throws<JsonException>(
            () => BrokerJson.DeserializeStrict<PolicyManagementResponse>(management.ToJsonString()));

        var replacement = JsonNode.Parse(await ReadFixture("requests", "policy-replacement.update.request.json"))!;
        replacement["ExpectedStoreToken"] = "";
        Assert.Throws<JsonException>(
            () => BrokerJson.DeserializeStrict<PolicyReplacementRequest>(replacement.ToJsonString()));
        replacement["ExpectedStoreToken"] = "store:active:7";
        replacement["ValidationReceipt"] = "";
        Assert.Throws<JsonException>(
            () => BrokerJson.DeserializeStrict<PolicyReplacementRequest>(replacement.ToJsonString()));
    }

    [Fact]
    public async Task Strict_validation_response_enforces_success_artifact_invariant()
    {
        var valid = JsonNode.Parse(await ReadFixture("responses", "policy-validation.valid.response.json"))!;
        valid["Validation"]!.AsObject().Remove("CanonicalDraft");
        Assert.Throws<JsonException>(
            () => BrokerJson.DeserializeStrict<PolicyValidationResponse>(valid.ToJsonString()));

        var invalid = JsonNode.Parse(await ReadFixture("responses", "policy-validation.invalid.response.json"))!;
        invalid["Validation"]!["ValidationReceipt"] = "unexpected-receipt";
        Assert.Throws<JsonException>(
            () => BrokerJson.DeserializeStrict<PolicyValidationResponse>(invalid.ToJsonString()));
    }

    [Theory]
    [InlineData("\"stalepolicystoretoken\"")]
    [InlineData("16")]
    public async Task Management_error_rejects_noncanonical_error_code(string value)
    {
        var error = JsonNode.Parse(await ReadFixture("responses", "policy-stale-token.error.json"))!;
        error["Code"] = JsonNode.Parse(value);

        Assert.Throws<JsonException>(() => BrokerJson.Deserialize<ErrorResponse>(error.ToJsonString()));
    }

    [Fact]
    public async Task Management_error_enforces_validation_result_invariant()
    {
        var error = JsonNode.Parse(await ReadFixture("responses", "policy-stale-token.error.json"))!;
        error["Validation"]!["IsValid"] = true;

        Assert.Throws<JsonException>(() => BrokerJson.Deserialize<ErrorResponse>(error.ToJsonString()));
    }

    private static async Task<string> ReadFixture(string directory, string file) =>
        await File.ReadAllTextAsync(Path.Combine(TestData.SamplesDir, directory, file));

    private static BrokerClient CreateClient(FakeBrokerTransport transport) => new(new BrokerClientOptions
    {
        Transport = transport,
        EffectiveUser = "DEVOLUTIONS\\bob",
        RequestedElevation = Elevation.Standard,
        ClientExecutablePath = "C:\\Tools\\client.exe",
        ClientVersion = "9.8.7",
    });

    private sealed class FakeBrokerTransport(params BrokerTransportResponse[] responses) : IBrokerTransport
    {
        private readonly Queue<BrokerTransportResponse> _responses = new(responses);

        public Transport Kind => Transport.HttpNamedPipe;

        public List<BrokerTransportRequest> Requests { get; } = [];

        public Task<BrokerTransportResponse> Send(
            BrokerTransportRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"No fake broker response queued for {request.Path}.");
            }

            return Task.FromResult(_responses.Dequeue());
        }

        public void Dispose()
        {
        }
    }
}