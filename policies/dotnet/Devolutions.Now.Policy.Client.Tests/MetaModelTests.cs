using System.Text.Json;

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

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PackageRequest>(json, TestData.Strict));
    }

    [Fact]
    public void RequestKind_is_required_on_deserialization()
    {
        const string json = """{"RequestVersion":"1.0"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PackageRequest>(json, TestData.Strict));
    }

    [Fact]
    public void ResponseKind_rejects_wrong_value_on_deserialization()
    {
        const string json =
            """
            {"ResponseKind":"ErrorResponse","ResponseVersion":"1.0","Server":{"ServerVersion":"mock","Transport":"HttpNamedPipe"},"Status":"Ready","PolicyId":"mock.policy"}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<HealthResponse>(json, TestData.Strict));
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

    private static async Task AssertSerializesValid<T>(T dto, string componentName)
    {
        var schema = await TestData.SchemaAsync(componentName);
        var json = JsonSerializer.Serialize(dto, BrokerJson.Options);

        // Output must satisfy the schema (catches missing required fields / type drift).
        var errors = schema.Validate(json);
        Assert.True(
            errors.Count == 0,
            $"Serialized {typeof(T).Name} failed {componentName} schema validation:\n" +
            string.Join("\n", errors.Select(e => $"  {e.Kind} at {e.Path}")));

        // Round-trip back through the DTO with strict mapping (catches schema fields the DTO drops).
        var reparsed = JsonSerializer.Deserialize<T>(json, TestData.Strict);
        Assert.NotNull(reparsed);
    }
}