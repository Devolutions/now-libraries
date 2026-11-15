using System.Text.Json;
using System.Text.Json.Nodes;

using Devolutions.Now.Policy.Api;

using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

/// <summary>Grant responses are validated identically by both deserialization helpers and by serialization.</summary>
public sealed class GrantSerializerEntryPointTests
{
    private const string Valid = """
        {"ResponseKind":"GrantExecutionResponse","ResponseVersion":"1.0",
         "Server":{"ServerVersion":"1.2.3","Transport":"HttpNamedPipe"},
         "RequestId":"0b3e6f0e-5d58-4a0e-9d2c-1f4f1c6c8a11",
         "ReceivedAt":"2026-11-15T05:07:42Z","CompletedAt":"2026-11-15T05:07:43Z",
         "Request":{"Manager":"Winget"},"Authorization":{"Kind":"RemoteGrant"},
         "Operation":{"OperationId":"op-1","Status":"Running","SubmittedAt":"2026-11-15T05:07:42Z"}}
        """;

    public static TheoryData<string> Malformed() => new()
    {
        Valid.Replace("\"Kind\":\"RemoteGrant\"", "\"Kind\":\"RemoteGrant\",\"Extra\":1"),
        Valid.Replace("\"Winget\"", "\"Winget\",\"Policy\":null"),
        Valid.Replace("\"Status\":\"Running\"", "\"Status\":\"Running\",\"Status\":\"Running\""),
        Valid.Replace("2026-11-15T05:07:43Z", "2026-11-15T14:07:43+09:00"),
        Valid.Replace("\"1.0\"", "\"1.1\""),
        Valid.Replace("\"Winget\"", "\"winget\""),
    };

    [Fact]
    public void Both_deserializers_accept_a_valid_response()
    {
        Assert.Equal(GrantAuthorizationKind.RemoteGrant, BrokerSerializer.Deserialize<GrantExecutionResponse>(Valid)!.Authorization.Kind);
        Assert.Equal("op-1", BrokerSerializer.DeserializeStrict<GrantExecutionResponse>(Valid)!.Operation.OperationId);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Both_deserializers_reject_malformed_responses(string json)
    {
        Assert.Throws<JsonException>(() => BrokerSerializer.Deserialize<GrantExecutionResponse>(json));
        Assert.Throws<JsonException>(() => BrokerSerializer.DeserializeStrict<GrantExecutionResponse>(json));
    }

    [Fact]
    public void Serialize_rejects_a_response_that_breaks_the_contract()
    {
        var response = BrokerSerializer.Deserialize<GrantExecutionResponse>(Valid)!;
        response.CompletedAt = response.CompletedAt.ToOffset(TimeSpan.FromHours(9));
        Assert.Throws<JsonException>(() => BrokerSerializer.Serialize(response));
        Assert.Throws<JsonException>(() => BrokerSerializer.Serialize(new GrantExecutionResponse()));
    }

    [Fact]
    public void Legacy_execution_response_still_ignores_unknown_members_when_not_strict()
    {
        var legacy = JsonNode.Parse(
            """{"ResponseKind":"ExecutionResponse","Unknown":1}""")!.ToJsonString();
        Assert.NotNull(BrokerSerializer.Deserialize<ExecutionResponse>(legacy));
        Assert.Throws<JsonException>(() => BrokerSerializer.DeserializeStrict<ExecutionResponse>(legacy));
    }
}