using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Devolutions.Now.Policy.Api;
using Devolutions.Now.Policy.Client;

using Xunit;

using PackageIdentifierCondition = Devolutions.Now.Policy.Model.PackageIdentifierCondition;
using PolicyConstraints = Devolutions.Now.Policy.Model.PolicyConstraints;
using PolicyDocument = Devolutions.Now.Policy.Model.PolicyDocument;
using PolicyDraftDocument = Devolutions.Now.Policy.Model.PolicyDraftDocument;
using PolicyDraftMetadata = Devolutions.Now.Policy.Model.PolicyDraftMetadata;
using PolicyMetadata = Devolutions.Now.Policy.Model.PolicyMetadata;
using VersionCondition = Devolutions.Now.Policy.Model.VersionCondition;
using VersionRange = Devolutions.Now.Policy.Model.VersionRange;

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
    public async Task Policy_contract_serializers_reject_duplicates_in_embedded_documents()
    {
        var policyResponse = WithEscapedPolicyFormatVersionDuplicate(
            await ReadFixture("responses", "policy.response.json"));
        var managementResponse = WithEscapedPolicyFormatVersionDuplicate(
            await ReadFixture("responses", "policy-management.active.response.json"));
        var validationRequest = WithEscapedPolicyFormatVersionDuplicate(
            await ReadFixture("requests", "policy-validation.request.json"));
        var validationResponse = WithEscapedPolicyFormatVersionDuplicate(
            await ReadFixture("responses", "policy-validation.valid.response.json"));
        var replacementRequest = WithEscapedPolicyFormatVersionDuplicate(
            await ReadFixture("requests", "policy-replacement.update.request.json"));
        var replacementResponse = WithEscapedPolicyFormatVersionDuplicate(
            await ReadFixture("responses", "policy-replacement.response.json"));
        var errorResponse = WithEscapedPolicyFormatVersionDuplicate(
            await ReadFixture("responses", "policy-stale-token.error.json"));

        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        Assert.Throws<JsonException>(() => BrokerSerializer.Deserialize<PolicyResponse>(policyResponse));
        Assert.Throws<JsonException>(() => BrokerSerializer.DeserializeStrict<PolicyResponse>(policyResponse));
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(managementResponse));
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyValidationRequest>(validationRequest));
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyValidationResponse>(validationResponse));
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(replacementRequest));
        Assert.Throws<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyReplacementResponse>(replacementResponse));
        Assert.Throws<JsonException>(() => BrokerSerializer.Deserialize<ErrorResponse>(errorResponse));

        foreach (var options in new[] { BrokerSerializer.Options, BrokerSerializer.PrettyOptions })
        {
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyResponse>(policyResponse, options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyManagementResponse>(managementResponse, options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyValidationRequest>(validationRequest, options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyValidationResponse>(validationResponse, options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyReplacementRequest>(replacementRequest, options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyReplacementResponse>(replacementResponse, options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<ErrorResponse>(errorResponse, options));
        }
    }

    [Fact]
    public void Public_broker_options_reject_duplicates_in_direct_policy_management_types()
    {
        const string Metadata = """
            {
              "Id": "first",
              "\u0049d": "second",
              "Publisher": "Test",
              "Revision": 1,
              "PublishedAt": "2026-01-01T00:00:00Z"
            }
            """;
        const string Finding = """
            {
              "FindingVersion": "1.0",
              "Severity": "Warning",
              "Code": "DefaultAllow",
              "Path": "",
              "Message": "first",
              "Message": "second"
            }
            """;
        const string Diagnostics = """
            {
              "DiagnosticsVersion": "1.0",
              "DiagnosticsVersion": "2.0",
              "Findings": []
            }
            """;

        foreach (var options in new[] { BrokerSerializer.Options, BrokerSerializer.PrettyOptions })
        {
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyMetadata>(Metadata, options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyFinding>(Finding, options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<InvalidPolicyDiagnostics>(Diagnostics, options));
        }
    }

    [Fact]
    public void Public_broker_options_enforce_standalone_policy_condition_invariants()
    {
        foreach (var options in new[] { BrokerSerializer.Options, BrokerSerializer.PrettyOptions })
        {
            foreach (var invalid in new[]
            {
                "{}",
                """{"Exact":["Microsoft.*"]}""",
                """{"Exact":["Git.Git"],"Patterns":["Git.*"]}""",
                """{"Exact":["Git.Git"],"Patterns":null}""",
            })
            {
                Assert.Throws<JsonException>(
                    () => JsonSerializer.Deserialize<PackageIdentifierCondition>(invalid, options));
            }

            foreach (var invalid in new[]
            {
                "{}",
                """{"Exact":[]}""",
                """{"Exact":["1.0.0"],"Range":{"MinVersion":"1.0.0"}}""",
                """{"Exact":["1.0.0"],"Range":null}""",
            })
            {
                Assert.Throws<JsonException>(
                    () => JsonSerializer.Deserialize<VersionCondition>(invalid, options));
            }

            Assert.Throws<JsonException>(
                () => JsonSerializer.Serialize(new PackageIdentifierCondition(), options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Serialize(new VersionCondition(), options));
            foreach (var invalid in new[]
            {
                "{}",
                """{"MinVersion":"not-semver"}""",
            })
            {
                Assert.Throws<JsonException>(
                    () => JsonSerializer.Deserialize<VersionRange>(invalid, options));
            }
            Assert.Throws<JsonException>(
                () => JsonSerializer.Serialize(new VersionRange(), options));
        }
    }

    [Fact]
    public async Task Public_broker_options_enforce_standalone_validity_windows()
    {
        const string Metadata = """
            {
              "Id": "validity.test",
              "Publisher": "Test",
              "Revision": 1,
              "PublishedAt": "2026-01-01T00:00:00Z",
              "ValidFrom": "2026-01-01T01:00:00+01:00",
              "ValidUntil": "2026-01-01T00:00:00Z"
            }
            """;
        const string DraftMetadata = """
            {
              "Id": "validity.test",
              "Publisher": "Test",
              "ValidFrom": "2026-01-01T00:30:00Z",
              "ValidUntil": "2026-01-01T01:00:00+01:00"
            }
            """;

        foreach (var options in new[] { BrokerSerializer.Options, BrokerSerializer.PrettyOptions })
        {
            var exception = Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyMetadata>(Metadata, options));
            Assert.Equal("$.ValidUntil", exception.Path);
            exception = Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyDraftMetadata>(DraftMetadata, options));
            Assert.Equal("$.ValidUntil", exception.Path);
            Assert.Throws<JsonException>(
                () => JsonSerializer.Serialize(
                    new PolicyDraftMetadata
                    {
                        Id = "validity.test",
                        Publisher = "Test",
                        ValidFrom = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                        ValidUntil = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    },
                    options));
        }

        var response = JsonNode.Parse(await ReadFixture("responses", "policy.response.json"))!;
        response["Policy"]!["Metadata"]![nameof(PolicyMetadata.ValidFrom)] = "2026-01-01T00:00:00Z";
        response["Policy"]!["Metadata"]![nameof(PolicyMetadata.ValidUntil)] = "2026-01-01T00:00:00Z";
        foreach (var options in new[] { BrokerSerializer.Options, BrokerSerializer.PrettyOptions })
        {
            var exception = Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyResponse>(response.ToJsonString(), options));
            Assert.Equal("$.Policy.Metadata.ValidUntil", exception.Path);
        }

        static void Invalidate(JsonNode metadata)
        {
            metadata[nameof(PolicyMetadata.ValidFrom)] = "2026-01-01T00:00:00Z";
            metadata[nameof(PolicyMetadata.ValidUntil)] = "2026-01-01T00:00:00Z";
        }

        var management = JsonNode.Parse(
            await ReadFixture("responses", "policy-management.active.response.json"))!;
        Invalidate(management["Management"]!["Policy"]!["Metadata"]!);
        var validation = JsonNode.Parse(
            await ReadFixture("responses", "policy-validation.valid.response.json"))!;
        Invalidate(validation["Validation"]!["CanonicalDraft"]!["Metadata"]!);
        var replacement = JsonNode.Parse(
            await ReadFixture("responses", "policy-replacement.response.json"))!;
        Invalidate(replacement["Validation"]!["CanonicalDraft"]!["Metadata"]!);
        var error = JsonNode.Parse(
            await ReadFixture("responses", "policy-stale-token.error.json"))!;
        Invalidate(error["Management"]!["Policy"]!["Metadata"]!);

        foreach (var (document, type, expectedPath) in new[]
        {
            (management, typeof(PolicyManagementResponse), "$.Management.Policy.Metadata.ValidUntil"),
            (validation, typeof(PolicyValidationResponse), "$.Validation.CanonicalDraft.Metadata.ValidUntil"),
            (replacement, typeof(PolicyReplacementResponse), "$.Validation.CanonicalDraft.Metadata.ValidUntil"),
            (error, typeof(ErrorResponse), "$.Management.Policy.Metadata.ValidUntil"),
        })
        {
            foreach (var options in new[] { BrokerSerializer.Options, BrokerSerializer.PrettyOptions })
            {
                var exception = Assert.Throws<JsonException>(
                    () => JsonSerializer.Deserialize(document.ToJsonString(), type, options));
                Assert.Equal(expectedPath, exception.Path);
            }
        }
    }

    [Fact]
    public void Public_broker_options_reject_duplicates_in_every_direct_broker_object_type()
    {
        var contractAssemblies = new[]
        {
            typeof(BrokerSerializer).Assembly,
            typeof(PolicyDocument).Assembly,
        };

        foreach (var options in new[] { BrokerSerializer.Options, BrokerSerializer.PrettyOptions })
        {
            var metadataOptions = new JsonSerializerOptions(options);
            metadataOptions.Converters.Clear();
            var resolver = Assert.IsAssignableFrom<IJsonTypeInfoResolver>(
                metadataOptions.TypeInfoResolver);
            var publicObjectTypes = contractAssemblies
                .SelectMany(assembly => assembly.ExportedTypes)
                .Where(type => type.IsClass && !type.IsAbstract)
                .Where(type => resolver.GetTypeInfo(type, metadataOptions)?.Kind == JsonTypeInfoKind.Object)
                .ToList();

            Assert.Contains(typeof(RequestSource), publicObjectTypes);
            Assert.Contains(typeof(ServerContext), publicObjectTypes);
            Assert.Contains(typeof(ManagerCapability), publicObjectTypes);
            Assert.Contains(typeof(ErrorDetail), publicObjectTypes);

            foreach (var type in publicObjectTypes)
            {
                AssertDirectOptionsRejectDuplicates(type, options);
            }
        }
    }

    [Fact]
    public void Duplicate_rejecting_broker_options_preserve_caller_strictness()
    {
        const string Request = """
            {
              "RequestKind": "PolicyValidationRequest",
              "RequestVersion": "1.0",
              "Draft": {},
              "Unexpected": true
            }
            """;
        var options = new JsonSerializerOptions(BrokerSerializer.Options)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PolicyValidationRequest>(Request, options));
    }

    [Fact]
    public void Invalid_surrogate_in_opaque_draft_remains_a_json_error()
    {
        const string Request = """
            {
              "RequestKind": "PolicyValidationRequest",
              "RequestVersion": "1.0",
              "Draft": {
                "\uD800": true
              }
            }
            """;

        Assert.ThrowsAny<JsonException>(
            () => BrokerSerializer.DeserializeStrict<PolicyValidationRequest>(Request));
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<PolicyValidationRequest>(
                Request,
                BrokerSerializer.Options));
    }

    [Theory]
    [InlineData("management")]
    [InlineData("validation")]
    [InlineData("replacement")]
    [InlineData("error")]
    public async Task BrokerClient_rejects_duplicate_policy_properties_in_management_responses(
        string operation)
    {
        var responseFixture = operation switch
        {
            "management" => "policy-management.active.response.json",
            "validation" => "policy-validation.valid.response.json",
            "replacement" => "policy-replacement.response.json",
            _ => "policy-stale-token.error.json",
        };
        var statusCode = operation == "error" ? 409 : 200;
        var body = WithEscapedPolicyFormatVersionDuplicate(
            await ReadFixture("responses", responseFixture));
        var client = CreateClient(new FakeBrokerTransport(
            new BrokerTransportResponse { StatusCode = statusCode, Body = body }));

        var exception = operation switch
        {
            "management" => await Assert.ThrowsAsync<BrokerClientException>(
                () => client.GetPolicyManagement()),
            "validation" => await Assert.ThrowsAsync<BrokerClientException>(
                () => client.ValidatePolicy(JsonDocument.Parse("{}").RootElement)),
            "replacement" => await Assert.ThrowsAsync<BrokerClientException>(
                async () => await client.ReplacePolicy(
                    BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(
                        await ReadFixture("requests", "policy-replacement.update.request.json"))!)),
            _ => await Assert.ThrowsAsync<BrokerClientException>(
                async () => await client.ReplacePolicy(
                    BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(
                        await ReadFixture("requests", "policy-replacement.update.request.json"))!)),
        };

        Assert.Equal(
            operation == "error"
                ? BrokerClientErrorKind.BrokerError
                : BrokerClientErrorKind.InvalidResponse,
            exception.Kind);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.Null(exception.BrokerError);
    }

    [Fact]
    public async Task BrokerClient_wraps_invalid_surrogate_names_in_structured_error_responses()
    {
        var body = await ReadFixture("responses", "policy-stale-token.error.json");
        body = body.Replace(
            "\"Message\": \"The configured policy changed after it was read.\",",
            "\"Message\": \"The configured policy changed after it was read.\",\n\"\\uD800\": true,",
            StringComparison.Ordinal);
        var client = CreateClient(new FakeBrokerTransport(
            new BrokerTransportResponse { StatusCode = 409, Body = body }));
        var request = BrokerSerializer.DeserializeStrict<PolicyReplacementRequest>(
            await ReadFixture("requests", "policy-replacement.update.request.json"))!;

        var exception = await Assert.ThrowsAsync<BrokerClientException>(
            () => client.ReplacePolicy(request));

        Assert.Equal(BrokerClientErrorKind.BrokerError, exception.Kind);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.Null(exception.BrokerError);
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
    public async Task Replacement_response_enforces_success_invariants()
    {
        var response = JsonNode.Parse(await ReadFixture("responses", "policy-replacement.response.json"))!;
        var invalidValidation = JsonNode.Parse(
            await ReadFixture("responses", "policy-validation.invalid.response.json"))!;
        var missingManagement = JsonNode.Parse(
            await ReadFixture("responses", "policy-management.missing.response.json"))!;
        var schema = await TestData.SchemaAsync("PolicyReplacementResponse");

        foreach (var invalid in new[]
        {
            ReplaceProperty(response, "Validation", invalidValidation["Validation"]!),
            ReplaceProperty(response, "Management", missingManagement["Management"]!),
        })
        {
            var json = invalid.ToJsonString();
            Assert.Throws<JsonException>(() => BrokerSerializer.Deserialize<PolicyReplacementResponse>(json));
            Assert.Throws<JsonException>(() => BrokerSerializer.DeserializeStrict<PolicyReplacementResponse>(json));
            Assert.NotEmpty(schema.Validate(json));
        }

        foreach (var invalid in new[]
        {
            MismatchedReplacementManagementPolicy(response),
            MismatchedReplacementCanonicalDraft(response),
        })
        {
            var json = invalid.ToJsonString();
            Assert.Throws<JsonException>(() => BrokerSerializer.Deserialize<PolicyReplacementResponse>(json));
            Assert.Throws<JsonException>(() => BrokerSerializer.DeserializeStrict<PolicyReplacementResponse>(json));
        }

        var dto = BrokerSerializer.DeserializeStrict<PolicyReplacementResponse>(response.ToJsonString())!;
        dto.Validation = BrokerSerializer.DeserializeStrict<PolicyValidationResponse>(
            invalidValidation.ToJsonString())!.Validation;
        Assert.Throws<JsonException>(() => BrokerSerializer.Serialize(dto));

        dto = BrokerSerializer.DeserializeStrict<PolicyReplacementResponse>(response.ToJsonString())!;
        dto.Management = BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(
            missingManagement.ToJsonString())!.Management;
        Assert.Throws<JsonException>(() => BrokerSerializer.Serialize(dto));

        dto = BrokerSerializer.DeserializeStrict<PolicyReplacementResponse>(response.ToJsonString())!;
        dto.Management.Policy!.Metadata.Publisher = "Other publisher";
        Assert.Throws<JsonException>(() => BrokerSerializer.Serialize(dto));

        dto = BrokerSerializer.DeserializeStrict<PolicyReplacementResponse>(response.ToJsonString())!;
        dto.Validation.CanonicalDraft!.Metadata.Publisher = "Other publisher";
        Assert.Throws<JsonException>(() => BrokerSerializer.Serialize(dto));
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
        var invalidValidationNode = JsonNode.Parse(invalidValidation)!;
        var invalidManagement = JsonNode.Parse(
            await ReadFixture(
                Path.Combine("invalid", "responses"),
                "policy-management.active-without-policy.response.json"))!;
        var management = BrokerSerializer.DeserializeStrict<PolicyManagementResponse>(
            await ReadFixture("responses", "policy-management.missing.response.json"))!;
        management.Management.State = PolicyManagementState.Active;

        foreach (var options in new[] { BrokerSerializer.Options, BrokerSerializer.PrettyOptions })
        {
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyValidationResponse>(invalidValidation, options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyValidationResult>(
                    invalidValidationNode["Validation"]!.ToJsonString(),
                    options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyManagementSnapshot>(
                    invalidManagement["Management"]!.ToJsonString(),
                    options));
            Assert.Throws<JsonException>(() => JsonSerializer.Serialize(management, options));
        }
    }

    [Fact]
    public async Task Public_serializer_options_enforce_policy_root_semantic_invariants()
    {
        var committed = JsonNode.Parse(await ReadFixture("responses", "policy.response.json"))!["Policy"]!;
        var draft = JsonNode.Parse(
            await ReadFixture("responses", "policy-validation.valid.response.json"))!["Validation"]!["CanonicalDraft"]!;

        foreach (var options in new[] { BrokerSerializer.Options, BrokerSerializer.PrettyOptions })
        {
            foreach (var invalid in InvalidCommittedPolicies(committed))
            {
                Assert.Throws<JsonException>(
                    () => JsonSerializer.Deserialize<PolicyDocument>(invalid.ToJsonString(), options));
            }

            foreach (var invalid in InvalidDraftPolicies(draft))
            {
                Assert.Throws<JsonException>(
                    () => JsonSerializer.Deserialize<PolicyDraftDocument>(invalid.ToJsonString(), options));
            }

            var committedDto = JsonSerializer.Deserialize<PolicyDocument>(committed.ToJsonString(), options)!;
            committedDto.Metadata.Revision = 0;
            Assert.Throws<JsonException>(() => JsonSerializer.Serialize(committedDto, options));

            committedDto = JsonSerializer.Deserialize<PolicyDocument>(committed.ToJsonString(), options)!;
            committedDto.Metadata.Revision = (uint)int.MaxValue + 1;
            Assert.Throws<JsonException>(() => JsonSerializer.Serialize(committedDto, options));

            var draftDto = JsonSerializer.Deserialize<PolicyDraftDocument>(draft.ToJsonString(), options)!;
            draftDto.Rules[0].Match.SkipHashCheck = null;
            draftDto.Rules[0].Match.Operations.Clear();
            Assert.DoesNotContain(
                "\"SkipHashCheck\"",
                JsonSerializer.Serialize(draftDto, options),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "\"Operations\"",
                JsonSerializer.Serialize(draftDto, options),
                StringComparison.Ordinal);

            committedDto = JsonSerializer.Deserialize<PolicyDocument>(committed.ToJsonString(), options)!;
            committedDto.Rules[0].Constraints = new PolicyConstraints { AllowInteractive = false };
            Assert.Throws<JsonException>(() => JsonSerializer.Serialize(committedDto, options));
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

    private static string WithEscapedPolicyFormatVersionDuplicate(string json) =>
        json.Replace(
            "\"PolicyFormatVersion\": \"1.0.0\",",
            "\"PolicyFormatVersion\": \"1.0.0\",\n\"PolicyFormatVersi\\u006fn\": \"1.0.0\",",
            StringComparison.Ordinal);

    private static void AssertDirectOptionsRejectDuplicates(
        Type type,
        JsonSerializerOptions options)
    {
        foreach (var json in new[]
        {
            """{"Duplicate":true,"\u0044uplicate":true}""",
            """{"Container":{"Duplicate":true,"\u0044uplicate":false}}""",
        })
        {
            var exception = Record.Exception(
                () => JsonSerializer.Deserialize(json, type, options));
            Assert.True(
                exception is JsonException,
                $"{type.FullName} did not reject duplicate properties: {exception}");
            var jsonException = (JsonException)exception;
            Assert.Contains(
                "Duplicate JSON property name",
                jsonException.Message,
                StringComparison.Ordinal);
        }
    }

    private static JsonNode ReplaceProperty(JsonNode source, string propertyName, JsonNode value)
    {
        var copy = source.DeepClone();
        copy[propertyName] = value.DeepClone();
        return copy;
    }

    private static JsonNode MismatchedReplacementManagementPolicy(JsonNode response)
    {
        var copy = response.DeepClone();
        copy["Management"]!["Policy"]!["Metadata"]!["Publisher"] = "Other publisher";
        return copy;
    }

    private static JsonNode MismatchedReplacementCanonicalDraft(JsonNode response)
    {
        var copy = response.DeepClone();
        copy["Validation"]!["CanonicalDraft"]!["Metadata"]!["Publisher"] = "Other publisher";
        return copy;
    }

    private static IEnumerable<JsonNode> InvalidCommittedPolicies(JsonNode committed)
    {
        var zeroRevision = committed.DeepClone();
        zeroRevision["Metadata"]!["Revision"] = 0;
        yield return zeroRevision;

        var excessiveRevision = committed.DeepClone();
        excessiveRevision["Metadata"]!["Revision"] = (uint)int.MaxValue + 1;
        yield return excessiveRevision;

        var mixedBooleanMatch = committed.DeepClone();
        mixedBooleanMatch["Rules"]![0]!["Match"]!["SkipHashCheck"] = new JsonArray(false, true);
        yield return mixedBooleanMatch;

        var constraintsOnDeny = committed.DeepClone();
        constraintsOnDeny["Rules"]![0]!["Constraints"] = new JsonObject { ["AllowInteractive"] = false };
        yield return constraintsOnDeny;
    }

    private static IEnumerable<JsonNode> InvalidDraftPolicies(JsonNode draft)
    {
        var mixedBooleanMatch = draft.DeepClone();
        mixedBooleanMatch["Rules"]![0]!["Match"]!["SkipHashCheck"] = new JsonArray(false, true);
        yield return mixedBooleanMatch;
    }

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