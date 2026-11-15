using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Devolutions.Now.Policy.Client;

using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

public class BrokerClientTests
{
    internal const string CapabilitiesResponse = """
        {"ResponseKind":"CapabilitiesResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"Transports":["HttpNamedPipe"],"Managers":[{"Manager":"Winget","Operations":["Install","Update","Uninstall"],"Scopes":["User","Machine"],"Architectures":["X86","X64","Arm64","Neutral"],"SupportsCustomParameters":true,"SupportsCustomInstallLocation":true,"SupportsCaptureOutput":true,"SupportsDetails":true},{"Manager":"PowerShell","Operations":["Install","Update","Uninstall"],"Scopes":["Machine"],"Architectures":["X64"],"SupportsCustomParameters":true,"SupportsCustomInstallLocation":false,"SupportsCaptureOutput":true,"SupportsDetails":false}],"MaxRequestBodyBytes":1048576}
        """;

    [Fact]
    public void DefaultPipeName_uses_api_constant()
    {
        Assert.Equal(BrokerApi.DefaultPipeName, BrokerClient.DefaultPipeName);
    }

    [Fact]
    public void Tests_run_with_reflection_json_disabled()
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
    }

    [Fact]
    public async Task Evaluate_populates_client_context_and_missing_metadata_before_sending()
    {
        var transport = new FakeBrokerTransport(
            CapabilitiesResponse,
            """
            {"ResponseKind":"EvaluationResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"RequestId":"unused","ReceivedAt":"2026-06-29T12:00:00Z","CompletedAt":"2026-06-29T12:00:01Z","Request":{},"Decision":{"Decision":"Allow","RuleId":"<default>","Reason":"allowed"},"WouldExecute":true,"Policy":{"Id":"mock.policy","Revision":1,"PolicyFormatVersion":"1.0.0"}}
            """);
        var client = new BrokerClient(new BrokerClientOptions
        {
            Transport = transport,
            EffectiveUser = "DEVOLUTIONS\\alice",
            RequestedElevation = Elevation.Elevated,
            ClientExecutablePath = "C:\\Tools\\client.exe",
            ClientVersion = "1.2.3",
        });
        var before = DateTimeOffset.UtcNow;

        _ = await client.Evaluate(new PackageOperationRequest
        {
            Operation = Operation.Install,
            Manager = ManagerName.Winget,
            Source = new RequestSource { Name = "winget" },
            Package = new RequestPackage { Id = "Microsoft.VisualStudioCode" },
            Options = new RequestOptions(),
        });
        var sent = transport.Requests[1];
        using var sentBody = JsonDocument.Parse(sent.Body!);

        Assert.Equal("/v1/capabilities", transport.Requests[0].Path);
        Assert.Equal("/v1/package-operations/evaluate", sent.Path);
        Assert.Equal(BrokerApi.PackageRequestKind, sentBody.RootElement.GetProperty("RequestKind").GetString());
        Assert.Equal(BrokerApi.Version, sentBody.RootElement.GetProperty("RequestVersion").GetString());
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", sentBody.RootElement.GetProperty("RequestId").GetString());
        Assert.InRange(sentBody.RootElement.GetProperty("CreatedAt").GetDateTimeOffset(), before, DateTimeOffset.UtcNow);
        Assert.DoesNotContain("Devolutions-Now-Request-Id", sent.Headers.Keys);

        var clientContext = sentBody.RootElement.GetProperty("Client");
        Assert.Equal("HttpNamedPipe", clientContext.GetProperty("Transport").GetString());
        Assert.Equal("Elevated", clientContext.GetProperty("RequestedElevation").GetString());
        Assert.Equal("DEVOLUTIONS\\alice", clientContext.GetProperty("EffectiveUser").GetString());
        Assert.Equal("C:\\Tools\\client.exe", clientContext.GetProperty("ClientExecutablePath").GetString());
        Assert.Equal("1.2.3", clientContext.GetProperty("ClientVersion").GetString());
        Assert.False(clientContext.TryGetProperty("ApiVersion", out _));
    }

    [Fact]
    public async Task Execute_normalizes_explicit_request_id_and_preserves_created_at()
    {
        var transport = new FakeBrokerTransport(
            CapabilitiesResponse,
            """
            {"ResponseKind":"ExecutionResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"RequestId":"6f8f1f54-8c42-4773-932a-ff7c7c9f58f1","ReceivedAt":"2026-06-29T12:00:00Z","CompletedAt":"2026-06-29T12:00:01Z","Request":{},"Decision":{"Decision":"Allow","RuleId":"<default>","Reason":"allowed"},"Policy":{"Id":"mock.policy","Revision":1,"PolicyFormatVersion":"1.0.0"},"Operation":{"OperationId":"operation:123","Status":"Starting","SubmittedAt":"2026-06-29T12:00:02Z"}}
            """);
        var client = CreateClient(transport);
        var createdAt = DateTimeOffset.Parse("2026-06-29T12:00:00Z");

        _ = await client.Execute(new PackageOperationRequest
        {
            RequestId = "{6F8F1F54-8C42-4773-932A-FF7C7C9F58F1}",
            CreatedAt = createdAt,
            Operation = Operation.Install,
            Manager = ManagerName.Winget,
            Source = new RequestSource { Name = "winget" },
            Package = new RequestPackage { Id = "Microsoft.VisualStudioCode" },
        });
        var sent = transport.Requests[1];
        using var sentBody = JsonDocument.Parse(sent.Body!);

        Assert.Equal("6f8f1f54-8c42-4773-932a-ff7c7c9f58f1", sentBody.RootElement.GetProperty("RequestId").GetString());
        Assert.Equal(createdAt, sentBody.RootElement.GetProperty("CreatedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task ExecuteAndWait_throws_typed_error_when_policy_denies_request()
    {
        var transport = new FakeBrokerTransport(
            CapabilitiesResponse,
            """
            {"ResponseKind":"ExecutionResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"RequestId":"6f8f1f54-8c42-4773-932a-ff7c7c9f58f1","ReceivedAt":"2026-06-29T12:00:00Z","CompletedAt":"2026-06-29T12:00:01Z","Request":{},"Decision":{"Decision":"Deny","RuleId":"block-rule","Reason":"blocked"},"Policy":{"Id":"mock.policy","Revision":1,"PolicyFormatVersion":"1.0.0"}}
            """);
        var client = CreateClient(transport);

        var exception = await Assert.ThrowsAsync<BrokerClientException>(() => client.ExecuteAndWait(new PackageOperationRequest
        {
            Operation = Operation.Install,
            Manager = ManagerName.Winget,
            Source = new RequestSource { Name = "winget" },
            Package = new RequestPackage { Id = "Microsoft.VisualStudioCode" },
        }));

        Assert.Equal(BrokerClientErrorKind.PolicyDenied, exception.Kind);
        Assert.Contains("blocked", exception.Message);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task QueryStatus_populates_client_context()
    {
        var transport = new FakeBrokerTransport(
            CapabilitiesResponse,
            """
            {"ResponseKind":"StatusResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"OperationId":"operation:123","RequestId":"b6cd88d1-9e32-49dd-b53f-e9dad34ad915","Status":"Completed"}
            """);
        var client = CreateClient(transport);

        _ = await client.QueryStatus(new OperationStatusQuery { OperationId = "operation:123" });
        var sent = transport.Requests[1];
        using var sentBody = JsonDocument.Parse(sent.Body!);

        Assert.Equal("/v1/capabilities", transport.Requests[0].Path);
        Assert.Equal("/v1/package-operations/get-status", sent.Path);
        Assert.Equal(BrokerApi.StatusRequestKind, sentBody.RootElement.GetProperty("RequestKind").GetString());
        Assert.Equal(BrokerApi.Version, sentBody.RootElement.GetProperty("RequestVersion").GetString());
        Assert.Equal("operation:123", sentBody.RootElement.GetProperty("OperationId").GetString());

        var clientContext = sentBody.RootElement.GetProperty("Client");
        Assert.Equal("HttpNamedPipe", clientContext.GetProperty("Transport").GetString());
        Assert.Equal("Standard", clientContext.GetProperty("RequestedElevation").GetString());
        Assert.Equal("DEVOLUTIONS\\bob", clientContext.GetProperty("EffectiveUser").GetString());
        Assert.Equal("C:\\Tools\\client.exe", clientContext.GetProperty("ClientExecutablePath").GetString());
        Assert.Equal("9.8.7", clientContext.GetProperty("ClientVersion").GetString());
        Assert.False(clientContext.TryGetProperty("ApiVersion", out _));
    }

    [Fact]
    public async Task Cancel_populates_client_context()
    {
        var transport = new FakeBrokerTransport(
            CapabilitiesResponse,
            """
            {"ResponseKind":"CancelResponse","ResponseVersion": "1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"OperationId":"operation:123","RequestId":"b6cd88d1-9e32-49dd-b53f-e9dad34ad915","Status":"Canceling","Message":"cancelation requested"}
            """);
        var client = CreateClient(transport);

        var response = await client.Cancel(new OperationCancelQuery { OperationId = "operation:123" });
        var sent = transport.Requests[1];
        using var sentBody = JsonDocument.Parse(sent.Body!);

        Assert.Equal("/v1/capabilities", transport.Requests[0].Path);
        Assert.Equal("/v1/package-operations/cancel", sent.Path);
        Assert.Equal(BrokerApi.CancelRequestKind, sentBody.RootElement.GetProperty("RequestKind").GetString());
        Assert.Equal(BrokerApi.Version, sentBody.RootElement.GetProperty("RequestVersion").GetString());
        Assert.Equal("operation:123", sentBody.RootElement.GetProperty("OperationId").GetString());
        Assert.Equal(OperationStatus.Canceling, response.Status);

        var clientContext = sentBody.RootElement.GetProperty("Client");
        Assert.Equal("HttpNamedPipe", clientContext.GetProperty("Transport").GetString());
        Assert.Equal("DEVOLUTIONS\\bob", clientContext.GetProperty("EffectiveUser").GetString());
    }

    [Fact]
    public async Task ExecuteAndWait_treats_canceled_status_as_terminal()
    {
        var transport = new FakeBrokerTransport(
            CapabilitiesResponse,
            """
            {"ResponseKind":"ExecutionResponse","ResponseVersion": "1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"RequestId":"6f8f1f54-8c42-4773-932a-ff7c7c9f58f1","ReceivedAt":"2026-06-29T12:00:00Z","CompletedAt":"2026-06-29T12:00:01Z","Request":{},"Decision":{"Decision":"Allow","RuleId":"<default>","Reason":"allowed"},"Policy":{"Id":"mock.policy","Revision":1,"PolicyFormatVersion":"1.0.0"},"Operation":{"OperationId":"operation:123","Status":"Starting","SubmittedAt":"2026-06-29T12:00:02Z"}}
            """,
            """
            {"ResponseKind":"StatusResponse","ResponseVersion": "1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"OperationId":"operation:123","RequestId":"6f8f1f54-8c42-4773-932a-ff7c7c9f58f1","Status":"Canceled","Message":"operation was canceled"}
            """);
        var client = CreateClient(transport);

        var status = await client.ExecuteAndWait(
            new PackageOperationRequest
            {
                Operation = Operation.Install,
                Manager = ManagerName.Winget,
                Source = new RequestSource { Name = "winget" },
                Package = new RequestPackage { Id = "Microsoft.VisualStudioCode" },
            },
            pollIntervalMs: 1);

        Assert.Equal(OperationStatus.Canceled, status.Status);
    }

    [Fact]
    public async Task ExecuteAndWait_requests_broker_cancelation_when_token_is_canceled()
    {
        var transport = new FakeBrokerTransport(
            CapabilitiesResponse,
            """
            {"ResponseKind":"ExecutionResponse","ResponseVersion": "1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"RequestId":"6f8f1f54-8c42-4773-932a-ff7c7c9f58f1","ReceivedAt":"2026-06-29T12:00:00Z","CompletedAt":"2026-06-29T12:00:01Z","Request":{},"Decision":{"Decision":"Allow","RuleId":"<default>","Reason":"allowed"},"Policy":{"Id":"mock.policy","Revision":1,"PolicyFormatVersion":"1.0.0"},"Operation":{"OperationId":"operation:123","Status":"Starting","SubmittedAt":"2026-06-29T12:00:02Z"}}
            """,
            """
            {"ResponseKind":"CancelResponse","ResponseVersion": "1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"OperationId":"operation:123","RequestId":"6f8f1f54-8c42-4773-932a-ff7c7c9f58f1","Status":"Canceling"}
            """);
        var client = CreateClient(transport);
        using var cts = new CancellationTokenSource();

        var pending = client.ExecuteAndWait(
            new PackageOperationRequest
            {
                Operation = Operation.Install,
                Manager = ManagerName.Winget,
                Source = new RequestSource { Name = "winget" },
                Package = new RequestPackage { Id = "Microsoft.VisualStudioCode" },
            },
            cts.Token,
            pollIntervalMs: 300_000);

        // Wait for the execute request to be sent, then cancel while the client is between polls.
        while (transport.Requests.Count < 2)
        {
            await Task.Delay(1);
        }
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal("/v1/package-operations/cancel", transport.Requests[^1].Path);
    }

    [Fact]
    public async Task Evaluate_rejects_unsupported_capability_before_operation_request()
    {
        var transport = new FakeBrokerTransport(CapabilitiesResponse);
        var client = CreateClient(transport);

        var exception = await Assert.ThrowsAsync<BrokerClientException>(() => client.Evaluate(new PackageOperationRequest
        {
            Operation = Operation.Install,
            Manager = ManagerName.PowerShell,
            Source = new RequestSource { Name = "PowerShellGet" },
            Package = new RequestPackage { Id = "Pester" },
            Options = new RequestOptions { CustomInstallLocation = "C:\\Tools" },
        }));

        Assert.Equal(BrokerClientErrorKind.UnsupportedCapability, exception.Kind);
        Assert.Contains("does not support custom install locations", exception.Message);
        Assert.Single(transport.Requests);
        Assert.Equal("/v1/capabilities", transport.Requests[0].Path);
    }

    [Fact]
    public async Task Evaluate_rejects_manager_not_advertised_by_broker_before_sending()
    {
        // The broker does not advertise Chocolatey in its capabilities.
        var transport = new FakeBrokerTransport(CapabilitiesResponse);
        var client = CreateClient(transport);

        var exception = await Assert.ThrowsAsync<BrokerClientException>(() => client.Evaluate(new PackageOperationRequest
        {
            Operation = Operation.Install,
            Manager = ManagerName.Chocolatey,
            Source = new RequestSource { Name = "chocolatey" },
            Package = new RequestPackage { Id = "git" },
        }));

        Assert.Equal(BrokerClientErrorKind.UnsupportedCapability, exception.Kind);
        Assert.Contains("does not advertise support", exception.Message);
        Assert.Single(transport.Requests);
        Assert.Equal("/v1/capabilities", transport.Requests[0].Path);
    }

    [Fact]
    public async Task GetHealth_throws_typed_error_for_broker_error_response()
    {
        var transport = new FakeBrokerTransport(new BrokerTransportResponse
        {
            StatusCode = 500,
            Body = """
                {"ResponseKind":"ErrorResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"Code":"InternalError","Message":"mock failure","Details":[{"Message":"details"}]}
                """,
        });
        var client = CreateClient(transport);

        var exception = await Assert.ThrowsAsync<BrokerClientException>(() => client.GetHealth());

        Assert.Equal(BrokerClientErrorKind.BrokerError, exception.Kind);
        Assert.Equal(500, exception.StatusCode);
        Assert.Equal(ErrorCode.InternalError, exception.BrokerError?.Code);
        Assert.Contains("mock failure", exception.Message);
    }

    [Fact]
    public async Task GetPolicy_sends_json_get_and_deserializes_response()
    {
        var body = await File.ReadAllTextAsync(Path.Combine(TestData.SamplesDir, "responses", "policy.response.json"));
        var transport = new FakeBrokerTransport(body);
        var client = CreateClient(transport);

        var response = await client.GetPolicy();

        var request = Assert.Single(transport.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/v1/policy", request.Path);
        Assert.Null(request.Body);
        Assert.Equal("application/json", request.Headers["Accept"]);
        Assert.Equal(BrokerApi.PolicyResponseKind, response.ResponseKind);
        Assert.Equal("contoso.desktop.standard-allowlist", response.Policy.Metadata.Id);
        Assert.Equal(4u, response.Policy.Metadata.Revision);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Policy.Metadata")]
    public async Task GetPolicy_rejects_unmapped_property(string objectPath)
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))
            ?? throw new InvalidOperationException("policy response sample should parse");
        var target = string.IsNullOrEmpty(objectPath) ? document : ResolveNode(document, objectPath);
        target.AsObject()["Unexpected"] = true;

        await AssertInvalidPolicyResponse(document);
    }

    [Fact]
    public async Task GetPolicy_rejects_duplicate_nested_policy_property_before_deserialization()
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var body = await File.ReadAllTextAsync(path);
        body = body.Replace(
            "\"PolicyFormatVersion\": \"1.0.0\",",
            "\"PolicyFormatVersion\": \"1.0.0\",\n\"PolicyFormatVersi\\u006fn\": \"1.0.0\",",
            StringComparison.Ordinal);
        var client = CreateClient(new FakeBrokerTransport(body));

        var exception = await Assert.ThrowsAsync<BrokerClientException>(() => client.GetPolicy());

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, exception.Kind);
        Assert.Equal("/v1/policy", exception.Endpoint);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task GetPolicy_wraps_invalid_surrogate_property_names()
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var body = await File.ReadAllTextAsync(path);
        body = body.Replace(
            "\"PolicyFormatVersion\": \"1.0.0\",",
            "\"PolicyFormatVersion\": \"1.0.0\",\n\"\\uD800\": true,",
            StringComparison.Ordinal);
        var client = CreateClient(new FakeBrokerTransport(body));

        var exception = await Assert.ThrowsAsync<BrokerClientException>(() => client.GetPolicy());

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, exception.Kind);
        Assert.Equal("/v1/policy", exception.Endpoint);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task GetPolicy_rejects_integer_policy_enum_token()
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))
            ?? throw new InvalidOperationException("policy response sample should parse");
        ResolveNode(document, "Policy.Rules.0.Match.Operations").AsArray()[0] = 0;

        await AssertInvalidPolicyResponse(document);
    }

    [Theory]
    [InlineData("Server.Transport", "httpnamedpipe")]
    [InlineData("Policy.Enforcement.DefaultDecision", "deny")]
    [InlineData("Policy.Rules.0.Decision", "deny")]
    [InlineData("Policy.Rules.0.Match.Operations.0", "install")]
    public async Task GetPolicy_rejects_noncanonical_enum_casing(string propertyPath, string value)
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))
            ?? throw new InvalidOperationException("policy response sample should parse");
        SetPropertyValue(document, propertyPath, value);

        await AssertInvalidPolicyResponse(document);
    }

    [Theory]
    [InlineData("Policy.Rules.0")]
    [InlineData("Policy.Rules.3.Match.SourceNames.0")]
    public async Task GetPolicy_rejects_null_collection_element(string elementPath)
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))
            ?? throw new InvalidOperationException("policy response sample should parse");
        SetPropertyToNull(document, elementPath);

        await AssertInvalidPolicyResponse(document);
    }

    [Fact]
    public async Task GetPolicy_rejects_constraints_on_deny_rule()
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))
            ?? throw new InvalidOperationException("policy response sample should parse");
        document["Policy"]!["Rules"]![0]!["Constraints"] =
            new JsonObject { ["AllowInteractive"] = false };

        await AssertInvalidPolicyResponse(document);
    }

    [Fact]
    public async Task GetPolicy_propagates_cancellation()
    {
        var transport = new FakeBrokerTransport(Array.Empty<string>());
        var client = CreateClient(transport);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetPolicy(cancellation.Token));
        Assert.Empty(transport.Requests);
    }

    [Theory]
    [InlineData("HttpNamedPipe")]
    [InlineData("httpnamedpipe")]
    public async Task GetPolicy_preserves_structured_unsupported_error(string transportValue)
    {
        var transport = new FakeBrokerTransport(new BrokerTransportResponse
        {
            StatusCode = 404,
            Body = """
                {"ResponseKind":"ErrorResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"Code":"NotFound","Message":"active policy inspection is not supported"}
                """.Replace("HttpNamedPipe", transportValue, StringComparison.Ordinal),
        });
        var client = CreateClient(transport);

        var exception = await Assert.ThrowsAsync<BrokerClientException>(() => client.GetPolicy());

        Assert.Equal(BrokerClientErrorKind.BrokerError, exception.Kind);
        Assert.Equal("/v1/policy", exception.Endpoint);
        Assert.Equal(404, exception.StatusCode);
        Assert.Equal(ErrorCode.NotFound, exception.BrokerError?.Code);
    }

    [Theory]
    [InlineData("ResponseVersion")]
    [InlineData("Server")]
    [InlineData("Server.ServerVersion")]
    [InlineData("Server.Transport")]
    [InlineData("Policy.PolicyFormatVersion")]
    [InlineData("Policy.Metadata.Id")]
    [InlineData("Policy.Enforcement.DefaultDecision")]
    [InlineData("Policy.Rules")]
    [InlineData("Policy.Rules.0.Match")]
    public async Task GetPolicy_rejects_missing_required_property(string propertyPath)
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))
            ?? throw new InvalidOperationException("policy response sample should parse");
        RemoveProperty(document, propertyPath);
        var client = CreateClient(new FakeBrokerTransport(document.ToJsonString()));

        var exception = await Assert.ThrowsAsync<BrokerClientException>(() => client.GetPolicy());

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, exception.Kind);
        Assert.Equal("/v1/policy", exception.Endpoint);
        Assert.Equal(200, exception.StatusCode);
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
    public async Task GetPolicy_rejects_null_non_nullable_property(string propertyPath)
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))
            ?? throw new InvalidOperationException("policy response sample should parse");
        SetPropertyToNull(document, propertyPath);
        var client = CreateClient(new FakeBrokerTransport(document.ToJsonString()));

        var exception = await Assert.ThrowsAsync<BrokerClientException>(() => client.GetPolicy());

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, exception.Kind);
        Assert.Equal("/v1/policy", exception.Endpoint);
        Assert.Equal(200, exception.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>not found</html>")]
    public async Task GetPolicy_identifies_legacy_unstructured_not_found(string body)
    {
        var transport = new FakeBrokerTransport(new BrokerTransportResponse { StatusCode = 404, Body = body });
        var client = CreateClient(transport);

        var exception = await Assert.ThrowsAsync<BrokerClientException>(() => client.GetPolicy());

        Assert.True(
            exception.Kind is BrokerClientErrorKind.EmptyResponse or BrokerClientErrorKind.BrokerError,
            $"unexpected legacy 404 error kind: {exception.Kind}");
        Assert.Equal("/v1/policy", exception.Endpoint);
        Assert.Equal(404, exception.StatusCode);
        Assert.Null(exception.BrokerError);
    }

    [Fact]
    public void Constructor_can_resolve_effective_user_automatically()
    {
        using var client = new BrokerClient(new BrokerClientOptions
        {
            RequestedElevation = Elevation.Standard,
            ClientExecutablePath = "C:\\Tools\\client.exe",
            ClientVersion = "9.8.7",
        });
    }

    private const string Grant = "g-r4nt.secret_value~1:x";
    private const string GrantRequestId = "0b3e6f0e-5d58-4a0e-9d2c-1f4f1c6c8a11";

    [Theory]
    [InlineData("")]
    [InlineData(".leading-dot")]
    [InlineData("has space")]
    [InlineData("na\u00efve")]
    [InlineData("line\r\nbreak")]
    [InlineData("tab\there")]
    public async Task ExecuteGrant_sends_nothing_for_invalid_grant(string grant)
    {
        var transport = new FakeAuthenticatedTransport();
        var client = CreateGrantClient(transport);

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.ExecuteGrant(GrantRequest(), grant));

        Assert.Equal(BrokerClientErrorKind.InvalidRequest, ex.Kind);
        Assert.True(ex.NoWorkStarted);
        Assert.Empty(transport.AllRequests);
    }

    [Fact]
    public async Task ExecuteGrant_rejects_grant_over_2048_characters_and_accepts_2048()
    {
        var transport = new FakeAuthenticatedTransport();
        var client = CreateGrantClient(transport);

        await Assert.ThrowsAsync<BrokerClientException>(() => client.ExecuteGrant(GrantRequest(), new string('a', 2049)));
        Assert.Empty(transport.AllRequests);

        _ = await client.ExecuteGrant(GrantRequest(), new string('a', 2048));
        Assert.Equal(new string('a', 2048), transport.GrantRequests.Single().Headers[BrokerApi.PackageGrantHeaderName]);
    }

    [Theory]
    [InlineData("0B3E6F0E-5D58-4A0E-9D2C-1F4F1C6C8A11", true, false)]
    [InlineData("{0b3e6f0e-5d58-4a0e-9d2c-1f4f1c6c8a11}", true, false)]
    [InlineData("0b3e6f0e5d584a0e9d2c1f4f1c6c8a11", true, false)]
    [InlineData(null, true, false)]
    [InlineData(GrantRequestId, false, false)]
    [InlineData(GrantRequestId, true, true)]
    public async Task ExecuteGrant_sends_nothing_for_invalid_request(string? requestId, bool captureOutput, bool includeCommandPreview)
    {
        var transport = new FakeAuthenticatedTransport();
        var client = CreateGrantClient(transport);

        var ex = await Assert.ThrowsAsync<BrokerClientException>(
            () => client.ExecuteGrant(GrantRequest(requestId, captureOutput, includeCommandPreview), Grant));

        Assert.Equal(BrokerClientErrorKind.InvalidRequest, ex.Kind);
        Assert.True(ex.NoWorkStarted);
        Assert.Empty(transport.AllRequests);
    }

    [Fact]
    public async Task ExecuteGrant_sends_nothing_without_server_authenticator()
    {
        var transport = new FakeAuthenticatedTransport();
        var client = CreateGrantClient(transport, withAuthenticator: false);

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.ExecuteGrant(GrantRequest(), Grant));

        Assert.Equal(BrokerClientErrorKind.UnsupportedCapability, ex.Kind);
        Assert.True(ex.NoWorkStarted);
        Assert.Empty(transport.AllRequests);
    }

    [Fact]
    public async Task ExecuteGrant_sends_nothing_over_transport_that_cannot_authenticate_the_server()
    {
        var transport = new FakeBrokerTransport(CapabilitiesResponse);
        var client = new BrokerClient(new BrokerClientOptions
        {
            Transport = transport,
            EffectiveUser = "DEVOLUTIONS\\bob",
            RequestedElevation = Elevation.Standard,
            ClientExecutablePath = "C:\\Tools\\client.exe",
            ClientVersion = "9.8.7",
            ServerAuthenticator = new AcceptingAuthenticator(),
        });

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.ExecuteGrant(GrantRequest(), Grant));

        Assert.Equal(BrokerClientErrorKind.UnsupportedCapability, ex.Kind);
        Assert.True(ex.NoWorkStarted);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task ExecuteGrant_posts_to_grant_route_with_one_grant_header_and_never_calls_execute()
    {
        var transport = new FakeAuthenticatedTransport();
        var client = CreateGrantClient(transport);

        _ = await client.ExecuteGrant(GrantRequest(), Grant);
        var sent = transport.GrantRequests.Single();
        using var body = JsonDocument.Parse(sent.Body!);

        Assert.Equal("POST", sent.Method);
        Assert.Equal("/v1/package-operations/execute-grant", sent.Path);
        Assert.Equal("application/json", sent.Headers["Content-Type"]);
        Assert.Equal("application/json", sent.Headers["Accept"]);
        Assert.Single(sent.Headers, header => header.Key.Equals("X-Devolutions-Package-Grant", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Grant, sent.Headers["X-Devolutions-Package-Grant"]);
        Assert.DoesNotContain(Grant, sent.Body);
        Assert.Equal(GrantRequestId, body.RootElement.GetProperty("RequestId").GetString());
        Assert.True(body.RootElement.GetProperty("CaptureOutput").GetBoolean());
        Assert.False(body.RootElement.GetProperty("IncludeCommandPreview").GetBoolean());
        Assert.DoesNotContain(transport.AllRequests, request => request.Path.EndsWith("/execute", StringComparison.Ordinal));
        Assert.DoesNotContain(transport.LegacyRequests, request => request.Headers.ContainsKey(BrokerApi.PackageGrantHeaderName));
    }

    [Fact]
    public async Task ExecuteGrant_returns_validated_response()
    {
        var transport = new FakeAuthenticatedTransport();
        var client = CreateGrantClient(transport);

        var response = await client.ExecuteGrant(GrantRequest(), Grant);

        Assert.Equal(GrantRequestId, response.RequestId);
        Assert.Equal("op-7f3a", response.Operation.OperationId);
        Assert.Equal(GrantAuthorizationKind.RemoteGrant, response.Authorization.Kind);
    }

    [Theory]
    [InlineData("\"RequestId\": \"0b3e6f0e-5d58-4a0e-9d2c-1f4f1c6c8a11\"", "\"RequestId\": \"1b3e6f0e-5d58-4a0e-9d2c-1f4f1c6c8a11\"")]
    [InlineData("\"Authorization\"", "\"Decision\": {\"Decision\": \"Allow\"}, \"Authorization\"")]
    [InlineData("\"ResponseVersion\": \"1.0\"", "\"ResponseVersion\": \"2.0\"")]
    public async Task ExecuteGrant_treats_mismatching_success_response_as_unknown_outcome(string find, string replace)
    {
        var body = GrantResponseJson().Replace(find, replace);
        Assert.NotEqual(GrantResponseJson(), body);
        var transport = new FakeAuthenticatedTransport(grantResponse: new BrokerTransportResponse { StatusCode = 200, Body = body });
        var client = CreateGrantClient(transport);

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.ExecuteGrant(GrantRequest(), Grant));

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, ex.Kind);
        Assert.False(ex.NoWorkStarted);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    public async Task ExecuteGrant_treats_404_as_unsupported_endpoint_without_fallback(string body)
    {
        var transport = new FakeAuthenticatedTransport(grantResponse: new BrokerTransportResponse { StatusCode = 404, Body = body });
        var client = CreateGrantClient(transport);

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.ExecuteGrant(GrantRequest(), Grant));

        Assert.Equal(BrokerClientErrorKind.UnsupportedEndpoint, ex.Kind);
        Assert.True(ex.NoWorkStarted);
        Assert.Equal(404, ex.StatusCode);
        Assert.Single(transport.GrantRequests);
        Assert.DoesNotContain(transport.AllRequests, request => request.Path.EndsWith("/execute", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(400, "BadRequest")]
    [InlineData(401, "Unauthenticated")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(409, "Conflict")]
    [InlineData(413, "PayloadTooLarge")]
    [InlineData(415, "UnsupportedMediaType")]
    [InlineData(422, "ValidationFailed")]
    [InlineData(501, "UnsupportedEndpoint")]
    public async Task ExecuteGrant_maps_table_rejection_to_no_work_started(int status, string code)
    {
        var transport = new FakeAuthenticatedTransport(
            grantResponse: new BrokerTransportResponse { StatusCode = status, Body = GrantErrorJson(code) });
        var client = CreateGrantClient(transport);

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.ExecuteGrant(GrantRequest(), Grant));

        Assert.Equal(BrokerClientErrorKind.BrokerError, ex.Kind);
        Assert.True(ex.NoWorkStarted);
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(Enum.Parse<ErrorCode>(code), ex.BrokerErrorCode);
        Assert.Null(ex.BrokerError);
    }

    [Theory]
    [InlineData(403, "BadRequest", "")]
    [InlineData(400, "BadRequest", ",\"Validation\":null")]
    [InlineData(400, "BadRequest", ",\"Management\":null")]
    [InlineData(400, "BadRequest", ",\"Unexpected\":1")]
    [InlineData(400, "BadRequest", ",\"Details\":[{\"Message\":\"\"}]")]
    [InlineData(400, "BadRequest", ",\"Code\":\"BadRequest\"")]
    [InlineData(500, "InternalError", "")]
    [InlineData(503, "InternalError", "")]
    [InlineData(504, "BadRequest", "")]
    public async Task ExecuteGrant_keeps_outcome_unknown_for_other_rejections(int status, string code, string extra)
    {
        var transport = new FakeAuthenticatedTransport(
            grantResponse: new BrokerTransportResponse { StatusCode = status, Body = GrantErrorJson(code, extra) });
        var client = CreateGrantClient(transport);

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.ExecuteGrant(GrantRequest(), Grant));

        Assert.False(ex.NoWorkStarted);
        Assert.Null(ex.BrokerErrorCode);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    public async Task ExecuteGrant_never_copies_response_text_or_grant_into_exception_or_trace(int status)
    {
        var message = $"echo {Grant} from the broker";
        var transport = new FakeAuthenticatedTransport(
            grantResponse: new BrokerTransportResponse { StatusCode = status, Body = GrantErrorJson(status == 400 ? "BadRequest" : "InternalError", message: message) });
        var trace = new List<string>();
        var client = CreateGrantClient(transport, trace: trace);

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.ExecuteGrant(GrantRequest(), Grant));

        Assert.DoesNotContain(Grant, ex.ToString());
        Assert.DoesNotContain("from the broker", ex.ToString());
        Assert.DoesNotContain(trace, line => line.Contains(Grant) || line.Contains("from the broker"));
    }

    [Fact]
    public void ServiceBrokerServerAuthenticator_accepts_running_service_process_in_session_0()
    {
        var authenticator = new ServiceBrokerServerAuthenticator("Svc", _ => (4, 1234));

        authenticator.Authenticate(new BrokerPipeServerIdentity(1234, 0));
    }

    [Theory]
    [InlineData(1u, 1234, 1234, 0)]
    [InlineData(4u, 1234, 4321, 0)]
    [InlineData(4u, 1234, 1234, 1)]
    [InlineData(4u, 0, 0, 0)]
    public void ServiceBrokerServerAuthenticator_rejects_unproven_server(uint state, uint servicePid, int serverPid, int session)
    {
        var authenticator = new ServiceBrokerServerAuthenticator("Svc", _ => (state, servicePid));

        Assert.ThrowsAny<Exception>(() => authenticator.Authenticate(new BrokerPipeServerIdentity(serverPid, session)));
    }

    [WindowsFact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task SendAuthenticated_authenticates_this_process_as_pipe_server_before_writing()
    {
        var pipeName = $"nowgt-{Guid.NewGuid():N}"[..22];
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var authenticator = new RecordingAuthenticator();
        var serving = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var buffer = new byte[4096];
            var total = 0;
            while (!Encoding.ASCII.GetString(buffer, 0, total).EndsWith("\r\n\r\n{}", StringComparison.Ordinal))
            {
                total += await server.ReadAsync(buffer.AsMemory(total));
            }

            var requestLine = Encoding.ASCII.GetString(buffer, 0, total);
            await server.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\n{}"));
            await server.FlushAsync();
            server.WaitForPipeDrain();
            server.Disconnect();
            return requestLine;
        });

        using var transport = new NamedPipeBrokerTransport(pipeName);
        var result = await transport.SendAuthenticated(
            new BrokerTransportRequest { Method = "POST", Path = "/probe", Body = "{}" },
            authenticator);
        var requestLine = await serving;

        Assert.StartsWith("POST /probe HTTP/1.1", requestLine);
        Assert.Equal(200, result.Response.StatusCode);
        Assert.Equal(Environment.ProcessId, authenticator.Seen!.Value.ProcessId);
        Assert.Equal(Process.GetCurrentProcess().SessionId, authenticator.Seen.Value.SessionId);
        Assert.Equal(authenticator.Seen, result.Server);
    }

    [WindowsFact]
    public async Task SendAuthenticated_writes_nothing_when_authenticator_rejects()
    {
        var pipeName = $"nowgt-{Guid.NewGuid():N}"[..22];
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serving = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            return await server.ReadAsync(new byte[4096]);
        });

        using var transport = new NamedPipeBrokerTransport(pipeName);
        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => transport.SendAuthenticated(
            new BrokerTransportRequest
            {
                Method = "POST",
                Path = "/probe",
                Body = "{}",
                Headers = new Dictionary<string, string> { [BrokerApi.PackageGrantHeaderName] = Grant },
            },
            new RejectingAuthenticator()));

        Assert.Equal(BrokerClientErrorKind.ServerAuthenticationFailed, ex.Kind);
        Assert.True(ex.NoWorkStarted);
        Assert.Equal(0, await serving);
    }

    internal static PackageOperationRequest GrantRequest(
        string? requestId = GrantRequestId,
        bool captureOutput = true,
        bool includeCommandPreview = false) => new()
        {
            RequestId = requestId,
            Operation = Operation.Install,
            Manager = ManagerName.Winget,
            Source = new RequestSource { Name = "winget" },
            Package = new RequestPackage { Id = "Microsoft.VisualStudioCode" },
            CaptureOutput = captureOutput,
            IncludeCommandPreview = includeCommandPreview,
        };

    internal static string GrantResponseJson(string? eventChannelPath = null)
    {
        var channel = eventChannelPath is null
            ? ""
            : $", \"EventChannel\": {{ \"Kind\": \"LocalPipe\", \"Path\": {JsonString(eventChannelPath)} }}";
        return $$"""
        {
          "ResponseKind": "GrantExecutionResponse",
          "ResponseVersion": "1.0",
          "Server": { "ServerVersion": "1.2.3", "Transport": "HttpNamedPipe" },
          "RequestId": "0b3e6f0e-5d58-4a0e-9d2c-1f4f1c6c8a11",
          "ReceivedAt": "2026-11-15T05:07:42Z",
          "CompletedAt": "2026-11-15T05:07:43Z",
          "Request": { "Manager": "Winget", "Source": "winget", "PackageId": "Microsoft.VisualStudioCode", "Operation": "Install" },
          "Authorization": { "Kind": "RemoteGrant" },
          "Operation": {
            "OperationId": "op-7f3a",
            "Status": "Running",
            "SubmittedAt": "2026-11-15T05:07:42Z"{{channel}}
          }
        }
        """;
    }

    private static string JsonString(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    internal static string GrantErrorJson(string code, string extra = "", string message = "rejected") => $$"""
        {"ResponseKind":"ErrorResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"1.2.3","Transport":"HttpNamedPipe"},"Code":"{{code}}","Message":{{JsonString(message)}}{{extra}}}
        """;

    internal static BrokerClient CreateGrantClient(
        FakeAuthenticatedTransport transport,
        List<string>? trace = null,
        bool withAuthenticator = true) => new(new BrokerClientOptions
        {
            Transport = transport,
            EffectiveUser = "DEVOLUTIONS\\bob",
            RequestedElevation = Elevation.Standard,
            ClientExecutablePath = "C:\\Tools\\client.exe",
            ClientVersion = "9.8.7",
            ServerAuthenticator = withAuthenticator ? new AcceptingAuthenticator() : null,
        })
        {
            Trace = trace is null ? null : trace.Add,
        };
    private static BrokerClient CreateClient(FakeBrokerTransport transport) => new(new BrokerClientOptions
    {
        Transport = transport,
        EffectiveUser = "DEVOLUTIONS\\bob",
        RequestedElevation = Elevation.Standard,
        ClientExecutablePath = "C:\\Tools\\client.exe",
        ClientVersion = "9.8.7",
    });

    private static async Task AssertInvalidPolicyResponse(JsonNode document)
    {
        var client = CreateClient(new FakeBrokerTransport(document.ToJsonString()));

        var exception = await Assert.ThrowsAsync<BrokerClientException>(() => client.GetPolicy());

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, exception.Kind);
        Assert.Equal("/v1/policy", exception.Endpoint);
        Assert.Equal(200, exception.StatusCode);
    }

    private static void RemoveProperty(JsonNode document, string propertyPath)
    {
        var segments = propertyPath.Split('.');
        var parent = document;
        foreach (var segment in segments[..^1])
        {
            parent = int.TryParse(segment, out var index)
                ? parent.AsArray()[index]!
                : parent[segment]!;
        }

        Assert.True(parent.AsObject().Remove(segments[^1]), $"missing fixture property {propertyPath}");
    }

    private static void SetPropertyToNull(JsonNode document, string propertyPath)
        => SetPropertyValue(document, propertyPath, null);

    private static void SetPropertyValue(JsonNode document, string propertyPath, JsonNode? value)
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
            parent.AsArray()[finalIndex] = value;
        }
        else
        {
            Assert.NotNull(parent.AsObject()[segments[^1]]);
            parent.AsObject()[segments[^1]] = value;
        }
    }

    private static JsonNode ResolveNode(JsonNode document, string propertyPath)
    {
        var node = document;
        foreach (var segment in propertyPath.Split('.'))
        {
            node = int.TryParse(segment, out var index)
                ? node.AsArray()[index]!
                : node[segment]!;
        }

        return node;
    }

    private sealed class FakeBrokerTransport : IBrokerTransport
    {
        private readonly Queue<BrokerTransportResponse> _responses;

        public FakeBrokerTransport(params string[] responseBodies)
            : this(responseBodies.Select(body => new BrokerTransportResponse { StatusCode = 200, Body = body }).ToArray())
        {
        }

        public FakeBrokerTransport(params BrokerTransportResponse[] responses)
        {
            _responses = new Queue<BrokerTransportResponse>(responses);
        }

        public Transport Kind => Transport.HttpNamedPipe;

        public List<BrokerTransportRequest> Requests { get; } = [];

        public Task<BrokerTransportResponse> Send(BrokerTransportRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
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
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows named pipes.";
        }
    }
}

internal sealed class AcceptingAuthenticator : IBrokerServerAuthenticator
{
    public void Authenticate(BrokerPipeServerIdentity server)
    {
    }
}

internal sealed class RejectingAuthenticator : IBrokerServerAuthenticator
{
    public void Authenticate(BrokerPipeServerIdentity server) => throw new InvalidOperationException("rejected");
}

internal sealed class RecordingAuthenticator : IBrokerServerAuthenticator
{
    public BrokerPipeServerIdentity? Seen { get; private set; }

    public void Authenticate(BrokerPipeServerIdentity server) => Seen = server;
}

/// <summary>Answers capabilities over the legacy path and the grant request over the authenticated path.</summary>
internal sealed class FakeAuthenticatedTransport : IAuthenticatedBrokerTransport
{
    private readonly BrokerTransportResponse _grantResponse;
    private readonly BrokerPipeServerIdentity _server;

    public FakeAuthenticatedTransport(BrokerTransportResponse? grantResponse = null, BrokerPipeServerIdentity? server = null)
    {
        _grantResponse = grantResponse ?? new BrokerTransportResponse { StatusCode = 200, Body = BrokerClientTests.GrantResponseJson() };
        _server = server ?? new BrokerPipeServerIdentity(Environment.ProcessId, 0);
    }

    public Transport Kind => Transport.HttpNamedPipe;

    public List<BrokerTransportRequest> LegacyRequests { get; } = [];

    public List<BrokerTransportRequest> GrantRequests { get; } = [];

    public IEnumerable<BrokerTransportRequest> AllRequests => LegacyRequests.Concat(GrantRequests);

    public Task<BrokerTransportResponse> Send(BrokerTransportRequest request, CancellationToken cancellationToken = default)
    {
        LegacyRequests.Add(request);
        return request.Path == "/v1/capabilities"
            ? Task.FromResult(new BrokerTransportResponse { StatusCode = 200, Body = BrokerClientTests.CapabilitiesResponse })
            : throw new InvalidOperationException($"Unexpected legacy request to {request.Path}.");
    }

    public Task<AuthenticatedBrokerTransportResponse> SendAuthenticated(
        BrokerTransportRequest request,
        IBrokerServerAuthenticator authenticator,
        CancellationToken cancellationToken = default)
    {
        GrantRequests.Add(request);
        return Task.FromResult(new AuthenticatedBrokerTransportResponse { Response = _grantResponse, Server = _server });
    }

    public void Dispose()
    {
    }
}