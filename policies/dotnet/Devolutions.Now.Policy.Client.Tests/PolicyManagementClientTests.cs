using System.Text;
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
        var request = BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(await ReadFixture("requests", fixture))!;
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
        var request = BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(
            await ReadFixture("requests", "policy-replacement.update.request.json"))!;
        var errorBody = await ReadFixture("responses", "policy-stale-token.error.json");
        var transport = new FakeBrokerTransport(new BrokerTransportResponse { StatusCode = 409, Body = errorBody });

        var exception = await Assert.ThrowsAsync<BrokerClientException>(
            () => CreateClient(transport).ReplacePolicy(request));

        Assert.Equal(ErrorCode.StalePolicyStoreToken, exception.BrokerError?.Code);
        Assert.Equal(PolicyFindingCode.InvalidFieldValue, exception.BrokerError?.Validation?.Findings[0].Code);
        Assert.Equal("store:active:9", exception.BrokerError?.Management?.StoreToken);
    }

    [Fact]
    public async Task ReplacePolicy_parses_unsupported_json_path_format()
    {
        var request = BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(
            await ReadFixture("requests", "policy-replacement.update.request.json"))!;
        var errorBody = await ReadFixture("responses", "policy-unsupported-format.error.json");
        var transport = new FakeBrokerTransport(new BrokerTransportResponse { StatusCode = 422, Body = errorBody });

        var exception = await Assert.ThrowsAsync<BrokerClientException>(
            () => CreateClient(transport).ReplacePolicy(request));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal(ErrorCode.UnsupportedPolicyFormat, exception.BrokerError?.Code);
        Assert.Equal(PolicyReadOnlyReason.UnsupportedFormat, exception.BrokerError?.Management?.ReadOnlyReason);
        Assert.EndsWith("now-policy.yaml", exception.BrokerError?.Management?.ConfiguredPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_policy_format_values_use_exact_case()
    {
        var management = JsonNode.Parse(
            await ReadFixture("responses", "policy-management.unsupported-format.response.json"))!;
        var parsedManagement = BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(management.ToJsonString())!;
        Assert.Equal(PolicyReadOnlyReason.UnsupportedFormat, parsedManagement.Management.ReadOnlyReason);
        Assert.Contains("\"ReadOnlyReason\":\"UnsupportedFormat\"", BrokerSerializer.Serialize(parsedManagement));

        management["Management"]!["ReadOnlyReason"] = JsonNode.Parse("\"unsupportedformat\"");
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(management.ToJsonString()));

        var error = JsonNode.Parse(await ReadFixture("responses", "policy-unsupported-format.error.json"))!;
        var parsedError = BrokerSerializer.DeserializeStrict<ErrorResponse>(error.ToJsonString())!;
        Assert.Equal(ErrorCode.UnsupportedPolicyFormat, parsedError.Code);
        Assert.Contains("\"Code\":\"UnsupportedPolicyFormat\"", BrokerSerializer.Serialize(parsedError));

        error["Code"] = JsonNode.Parse("\"unsupportedpolicyformat\"");
        Assert.Throws<JsonException>(() => BrokerSerializer.DeserializeStrict<ErrorResponse>(error.ToJsonString()));
    }

    [Fact]
    public async Task Policy_management_requests_accept_the_exact_full_body_limit()
    {
        var validationBody = await ReadFixture("responses", "policy-validation.valid.response.json");
        var validationTransport = new FakeBrokerTransport(
            new BrokerTransportResponse { StatusCode = 200, Body = validationBody });
        var validationRequest = new PolicyValidationRequest { RequestVersion = BrokerApi.Version };
        var validationDraft = DraftForSerializedRequestSize(
            validationRequest,
            static (request, draft) => request.Draft = draft,
            BrokerApi.MaxPolicyManagementBodyBytes);

        await CreateClient(validationTransport).ValidatePolicy(validationDraft);

        Assert.Equal(
            BrokerApi.MaxPolicyManagementBodyBytes,
            Encoding.UTF8.GetByteCount(Assert.Single(validationTransport.Requests).Body!));

        var replacementRequest = BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(
            await ReadFixture("requests", "policy-replacement.update.request.json"))!;
        replacementRequest.Draft = DraftForSerializedRequestSize(
            replacementRequest,
            static (request, draft) => request.Draft = draft,
            BrokerApi.MaxPolicyManagementBodyBytes);
        var replacementBody = await ReadFixture("responses", "policy-replacement.response.json");
        var replacementTransport = new FakeBrokerTransport(
            new BrokerTransportResponse { StatusCode = 200, Body = replacementBody });

        await CreateClient(replacementTransport).ReplacePolicy(replacementRequest);

        Assert.Equal(
            BrokerApi.MaxPolicyManagementBodyBytes,
            Encoding.UTF8.GetByteCount(Assert.Single(replacementTransport.Requests).Body!));
    }

    [Fact]
    public async Task Policy_management_requests_reject_one_byte_over_before_transport()
    {
        var validationTransport = new FakeBrokerTransport();
        var validationRequest = new PolicyValidationRequest { RequestVersion = BrokerApi.Version };
        var validationDraft = DraftForSerializedRequestSize(
            validationRequest,
            static (request, draft) => request.Draft = draft,
            BrokerApi.MaxPolicyManagementBodyBytes + 1);

        var validationException = await Assert.ThrowsAsync<BrokerClientException>(
            () => CreateClient(validationTransport).ValidatePolicy(validationDraft));
        Assert.Equal(BrokerClientErrorKind.RequestTooLarge, validationException.Kind);
        Assert.Empty(validationTransport.Requests);

        var replacementRequest = BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(
            await ReadFixture("requests", "policy-replacement.update.request.json"))!;
        replacementRequest.Draft = DraftForSerializedRequestSize(
            replacementRequest,
            static (request, draft) => request.Draft = draft,
            BrokerApi.MaxPolicyManagementBodyBytes + 1);
        var replacementTransport = new FakeBrokerTransport();

        var replacementException = await Assert.ThrowsAsync<BrokerClientException>(
            () => CreateClient(replacementTransport).ReplacePolicy(replacementRequest));
        Assert.Equal(BrokerClientErrorKind.RequestTooLarge, replacementException.Kind);
        Assert.Empty(replacementTransport.Requests);
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
                async () => await client.ReplacePolicy(BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(
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
            () => BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(document.ToJsonString()));
    }

    [Fact]
    public async Task Strict_management_contract_rejects_empty_tokens_and_receipts()
    {
        var management = JsonNode.Parse(await ReadFixture("responses", "policy-management.active.response.json"))!;
        management["Management"]!["StoreToken"] = "";
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(management.ToJsonString()));

        var replacement = JsonNode.Parse(await ReadFixture("requests", "policy-replacement.update.request.json"))!;
        replacement["ExpectedStoreToken"] = "";
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(replacement.ToJsonString()));
        replacement["ExpectedStoreToken"] = "store:active:7";
        replacement["ValidationReceipt"] = "";
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(replacement.ToJsonString()));
    }

    [Fact]
    public async Task Opaque_tokens_and_receipts_reject_non_ascii_values()
    {
        var management = JsonNode.Parse(await ReadFixture("responses", "policy-management.active.response.json"))!;
        management["Management"]!["StoreToken"] = "store:activé:7";
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(management.ToJsonString()));
        Assert.NotEmpty(
            (await TestData.SchemaAsync("PolicyManagementResponse")).Validate(management.ToJsonString()));

        var replacement = JsonNode.Parse(await ReadFixture("requests", "policy-replacement.update.request.json"))!;
        replacement["ValidationReceipt"] = "receipt:é";
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(replacement.ToJsonString()));
        Assert.NotEmpty(
            (await TestData.SchemaAsync("PolicyReplacementRequest")).Validate(replacement.ToJsonString()));

        var managementDto = BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(
            await ReadFixture("responses", "policy-management.active.response.json"))!;
        managementDto.Management.StoreToken = "store:activé:7";
        Assert.Throws<JsonException>(() => BrokerSerializer.Serialize(managementDto));

        var replacementDto = BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(
            await ReadFixture("requests", "policy-replacement.update.request.json"))!;
        replacementDto.ValidationReceipt = "receipt:é";
        Assert.Throws<JsonException>(() => BrokerSerializer.Serialize(replacementDto));
    }

    [Fact]
    public async Task Strict_validation_response_enforces_success_artifact_invariant()
    {
        var valid = JsonNode.Parse(await ReadFixture("responses", "policy-validation.valid.response.json"))!;
        valid["Validation"]!.AsObject().Remove("CanonicalDraft");
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyValidationResponse>(valid.ToJsonString()));

        var invalid = JsonNode.Parse(await ReadFixture("responses", "policy-validation.invalid.response.json"))!;
        invalid["Validation"]!["ValidationReceipt"] = "unexpected-receipt";
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyValidationResponse>(invalid.ToJsonString()));

        var validWithNull = JsonNode.Parse(await ReadFixture("responses", "policy-validation.valid.response.json"))!;
        validWithNull["Validation"]!["CanonicalDraft"] = null;
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyValidationResponse>(validWithNull.ToJsonString()));
        Assert.NotEmpty(
            (await TestData.SchemaAsync("PolicyValidationResponse")).Validate(validWithNull.ToJsonString()));
    }

    [Fact]
    public async Task Active_management_snapshot_rejects_explicit_null_policy()
    {
        var active = JsonNode.Parse(await ReadFixture("responses", "policy-management.active.response.json"))!;
        active["Management"]!["Policy"] = null;

        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(active.ToJsonString()));
        Assert.NotEmpty(
            (await TestData.SchemaAsync("PolicyManagementResponse")).Validate(active.ToJsonString()));
    }

    [Fact]
    public async Task Optional_nulls_match_absent_values_in_none_states()
    {
        var invalid = JsonNode.Parse(await ReadFixture("responses", "policy-validation.invalid.response.json"))!;
        invalid["Validation"]!["CanonicalDraft"] = null;
        invalid["Validation"]!["ValidationReceipt"] = null;
        Assert.NotNull(BrokerSerializer.DeserializeStrict<PolicyValidationResponse>(invalid.ToJsonString()));
        Assert.Empty((await TestData.SchemaAsync("PolicyValidationResponse")).Validate(invalid.ToJsonString()));

        var missing = JsonNode.Parse(await ReadFixture("responses", "policy-management.missing.response.json"))!;
        missing["Management"]!["Policy"] = null;
        missing["Management"]!["InvalidDiagnostics"] = null;
        missing["Management"]!["ReadOnlyReason"] = null;
        Assert.NotNull(BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(missing.ToJsonString()));
        Assert.Empty((await TestData.SchemaAsync("PolicyManagementResponse")).Validate(missing.ToJsonString()));
    }

    [Theory]
    [InlineData("policy-validation.valid-with-error.response.json")]
    [InlineData("policy-validation.invalid-with-warning.response.json")]
    [InlineData("policy-validation.invalid-with-empty-findings.response.json")]
    public async Task Strict_validation_response_rejects_contradictory_findings(string fixture)
    {
        var json = await ReadFixture(Path.Combine("invalid", "responses"), fixture);
        Assert.Throws<JsonException>(() => BrokerSerializer.DeserializeStrict<PolicyValidationResponse>(json));
    }

    [Theory]
    [InlineData("policy-management.active-without-policy.response.json")]
    [InlineData("policy-management.invalid-with-warning.response.json")]
    [InlineData("policy-management.readonly-without-reason.response.json")]
    public async Task Strict_management_response_rejects_contradictory_snapshot(string fixture)
    {
        var json = await ReadFixture(Path.Combine("invalid", "responses"), fixture);
        Assert.Throws<JsonException>(() => BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(json));
    }

    [Fact]
    public async Task Management_contract_rejects_null_finding_elements()
    {
        var validation = JsonNode.Parse(await ReadFixture("responses", "policy-validation.valid.response.json"))!;
        validation["Validation"]!["Findings"] = new JsonArray((JsonNode?)null);
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyValidationResponse>(validation.ToJsonString()));
        Assert.Throws<JsonException>(
            () => BrokerSerializer.Deserialize<PolicyValidationResponse>(validation.ToJsonString()));

        var management = JsonNode.Parse(await ReadFixture("responses", "policy-management.invalid.response.json"))!;
        management["Management"]!["InvalidDiagnostics"]!["Findings"] = new JsonArray((JsonNode?)null);
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(management.ToJsonString()));
        Assert.Throws<JsonException>(
            () => BrokerSerializer.Deserialize<PolicyManagementResponse>(management.ToJsonString()));
    }

    [Theory]
    [InlineData("policy-validation.valid-with-error.response.json", true)]
    [InlineData("policy-validation.invalid-with-warning.response.json", true)]
    [InlineData("policy-management.active-without-policy.response.json", false)]
    [InlineData("policy-management.readonly-without-reason.response.json", false)]
    public async Task Non_strict_deserialization_still_enforces_semantic_invariants(string fixture, bool validation)
    {
        var json = await ReadFixture(Path.Combine("invalid", "responses"), fixture);
        if (validation)
        {
            Assert.Throws<JsonException>(() => BrokerSerializer.Deserialize<PolicyValidationResponse>(json));
        }
        else
        {
            Assert.Throws<JsonException>(() => BrokerSerializer.Deserialize<PolicyManagementResponse>(json));
        }
    }

    [Fact]
    public async Task Non_strict_replacement_deserialization_enforces_nested_invariants()
    {
        var response = JsonNode.Parse(await ReadFixture("responses", "policy-replacement.response.json"))!;
        response["Validation"]!["IsValid"] = false;
        response["Validation"]!.AsObject().Remove("CanonicalDraft");
        response["Validation"]!.AsObject().Remove("ValidationReceipt");

        Assert.Throws<JsonException>(
            () => BrokerSerializer.Deserialize<PolicyReplacementResponse>(response.ToJsonString()));
    }

    [Theory]
    [InlineData("policy-validation.valid-with-error.response.json", "PolicyValidationResponse")]
    [InlineData("policy-validation.invalid-with-warning.response.json", "PolicyValidationResponse")]
    [InlineData("policy-validation.invalid-with-empty-findings.response.json", "PolicyValidationResponse")]
    [InlineData("policy-management.active-without-policy.response.json", "PolicyManagementResponse")]
    [InlineData("policy-management.invalid-with-warning.response.json", "PolicyManagementResponse")]
    [InlineData("policy-management.readonly-without-reason.response.json", "PolicyManagementResponse")]
    public async Task OpenApi_rejects_contradictory_policy_management_contracts(string fixture, string component)
    {
        var json = await ReadFixture(Path.Combine("invalid", "responses"), fixture);
        var schema = await TestData.SchemaAsync(component);
        Assert.NotEmpty(schema.Validate(json));
    }

    [Fact]
    public async Task Serialization_rejects_contradictory_policy_management_contracts()
    {
        var validation = BrokerSerializer.DeserializeStrict<PolicyValidationResponse>(
            await ReadFixture("responses", "policy-validation.invalid.response.json"))!;
        validation.Validation.Findings.Clear();
        Assert.Throws<JsonException>(() => BrokerSerializer.Serialize(validation));

        var management = BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(
            await ReadFixture("responses", "policy-management.missing.response.json"))!;
        management.Management.State = PolicyManagementState.Active;
        Assert.Throws<JsonException>(() => BrokerSerializer.Serialize(management));
    }

    [Fact]
    public async Task Public_serializer_options_enforce_semantic_invariants()
    {
        var invalidValidation = await ReadFixture(
            Path.Combine("invalid", "responses"),
            "policy-validation.valid-with-error.response.json");
        var management = BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(
            await ReadFixture("responses", "policy-management.missing.response.json"))!;
        management.Management.State = PolicyManagementState.Active;

        foreach (var options in new[] { BrokerSerializer.Options, BrokerSerializer.PrettyOptions })
        {
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyValidationResponse>(invalidValidation, options));
            Assert.Throws<JsonException>(() => JsonSerializer.Serialize(management, options));
        }
    }

    [Theory]
    [InlineData("\"stalepolicystoretoken\"")]
    [InlineData("16")]
    public async Task Management_error_rejects_noncanonical_error_code(string value)
    {
        var error = JsonNode.Parse(await ReadFixture("responses", "policy-stale-token.error.json"))!;
        error["Code"] = JsonNode.Parse(value);

        Assert.Throws<JsonException>(() => BrokerSerializer.Deserialize<ErrorResponse>(error.ToJsonString()));
    }

    [Fact]
    public async Task Management_error_enforces_validation_result_invariant()
    {
        var error = JsonNode.Parse(await ReadFixture("responses", "policy-stale-token.error.json"))!;
        error["Validation"]!["IsValid"] = true;

        Assert.Throws<JsonException>(() => BrokerSerializer.Deserialize<ErrorResponse>(error.ToJsonString()));
    }

    [Fact]
    public async Task Stale_token_error_requires_atomic_management_snapshot()
    {
        var error = JsonNode.Parse(await ReadFixture("responses", "policy-stale-token.error.json"))!;
        error.AsObject().Remove("Management");

        Assert.Throws<JsonException>(() => BrokerSerializer.Deserialize<ErrorResponse>(error.ToJsonString()));
        Assert.NotEmpty((await TestData.SchemaAsync("ErrorResponse")).Validate(error.ToJsonString()));

        var contradictory = JsonNode.Parse(await ReadFixture("responses", "policy-stale-token.error.json"))!;
        contradictory["Management"]!.AsObject().Remove("Policy");
        Assert.Throws<JsonException>(() => BrokerSerializer.Deserialize<ErrorResponse>(contradictory.ToJsonString()));

        var dto = new ErrorResponse
        {
            Code = ErrorCode.StalePolicyStoreToken,
            Message = "stale",
        };
        Assert.Throws<JsonException>(() => BrokerSerializer.Serialize(dto));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>not found</html>")]
    public async Task GetPolicyManagement_preserves_legacy_route_not_found(string body)
    {
        var transport = new FakeBrokerTransport(new BrokerTransportResponse { StatusCode = 404, Body = body });

        var exception = await Assert.ThrowsAsync<BrokerClientException>(
            () => CreateClient(transport).GetPolicyManagement());

        Assert.True(
            exception.Kind is BrokerClientErrorKind.EmptyResponse or BrokerClientErrorKind.BrokerError,
            $"unexpected legacy 404 error kind: {exception.Kind}");
        Assert.Equal(404, exception.StatusCode);
        Assert.Null(exception.BrokerError);
    }

    private static async Task<string> ReadFixture(string directory, string file) =>
        await File.ReadAllTextAsync(Path.Combine(TestData.SamplesDir, directory, file));

    private static JsonElement DraftForSerializedRequestSize<TRequest>(
        TRequest request,
        Action<TRequest, JsonElement> setDraft,
        int targetSize)
    {
        using var emptyDraft = JsonDocument.Parse("""{"Padding":""}""");
        setDraft(request, emptyDraft.RootElement.Clone());
        var baseSize = Encoding.UTF8.GetByteCount(BrokerSerializer.Serialize(request));
        var paddingLength = targetSize - baseSize;
        Assert.True(paddingLength >= 0);

        using var paddedDraft = JsonDocument.Parse($$"""{"Padding":"{{new string('x', paddingLength)}}"}""");
        return paddedDraft.RootElement.Clone();
    }

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