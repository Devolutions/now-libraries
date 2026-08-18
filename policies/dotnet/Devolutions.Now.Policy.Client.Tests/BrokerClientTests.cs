using System.Text.Json;
using System.Text.Json.Nodes;

using Devolutions.Now.Policy.Client;

using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

public class BrokerClientTests
{
    private const string CapabilitiesResponse = """
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
            {"ResponseKind":"EvaluationResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"RequestId":"unused","ReceivedAt":"2026-06-29T12:00:00Z","CompletedAt":"2026-06-29T12:00:01Z","Request":{},"Decision":{"Decision":"Allow","RuleId":"<default>","Reason":"allowed"},"WouldExecute":true,"Policy":{"Id":"mock.policy","Revision":1,"PolicyVersion":"1.0.0"}}
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
            {"ResponseKind":"ExecutionResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"RequestId":"6f8f1f54-8c42-4773-932a-ff7c7c9f58f1","ReceivedAt":"2026-06-29T12:00:00Z","CompletedAt":"2026-06-29T12:00:01Z","Request":{},"Decision":{"Decision":"Allow","RuleId":"<default>","Reason":"allowed"},"Policy":{"Id":"mock.policy","Revision":1,"PolicyVersion":"1.0.0"},"Operation":{"OperationId":"operation:123","Status":"Starting","SubmittedAt":"2026-06-29T12:00:02Z"}}
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
            {"ResponseKind":"ExecutionResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"RequestId":"6f8f1f54-8c42-4773-932a-ff7c7c9f58f1","ReceivedAt":"2026-06-29T12:00:00Z","CompletedAt":"2026-06-29T12:00:01Z","Request":{},"Decision":{"Decision":"Deny","RuleId":"block-rule","Reason":"blocked"},"Policy":{"Id":"mock.policy","Revision":1,"PolicyVersion":"1.0.0"}}
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
            {"ResponseKind":"ExecutionResponse","ResponseVersion": "1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"RequestId":"6f8f1f54-8c42-4773-932a-ff7c7c9f58f1","ReceivedAt":"2026-06-29T12:00:00Z","CompletedAt":"2026-06-29T12:00:01Z","Request":{},"Decision":{"Decision":"Allow","RuleId":"<default>","Reason":"allowed"},"Policy":{"Id":"mock.policy","Revision":1,"PolicyVersion":"1.0.0"},"Operation":{"OperationId":"operation:123","Status":"Starting","SubmittedAt":"2026-06-29T12:00:02Z"}}
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
            {"ResponseKind":"ExecutionResponse","ResponseVersion": "1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"RequestId":"6f8f1f54-8c42-4773-932a-ff7c7c9f58f1","ReceivedAt":"2026-06-29T12:00:00Z","CompletedAt":"2026-06-29T12:00:01Z","Request":{},"Decision":{"Decision":"Allow","RuleId":"<default>","Reason":"allowed"},"Policy":{"Id":"mock.policy","Revision":1,"PolicyVersion":"1.0.0"},"Operation":{"OperationId":"operation:123","Status":"Starting","SubmittedAt":"2026-06-29T12:00:02Z"}}
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
    public async Task GetPolicy_rejects_integer_policy_enum_token()
    {
        var path = Path.Combine(TestData.SamplesDir, "responses", "policy.response.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))
            ?? throw new InvalidOperationException("policy response sample should parse");
        ResolveNode(document, "Policy.Rules.0.Match.Operations").AsArray()[0] = 0;

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

    [Fact]
    public async Task GetPolicy_preserves_structured_unsupported_error()
    {
        var transport = new FakeBrokerTransport(new BrokerTransportResponse
        {
            StatusCode = 404,
            Body = """
                {"ResponseKind":"ErrorResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"Code":"NotFound","Message":"active policy inspection is not supported"}
                """,
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
    [InlineData("Policy.$schema")]
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
    {
        var segments = propertyPath.Split('.');
        var parent = document;
        foreach (var segment in segments[..^1])
        {
            parent = int.TryParse(segment, out var index)
                ? parent.AsArray()[index]!
                : parent[segment]!;
        }

        var property = parent.AsObject();
        Assert.NotNull(property[segments[^1]]);
        property[segments[^1]] = null;
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