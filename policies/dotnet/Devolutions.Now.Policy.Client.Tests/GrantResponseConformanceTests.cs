using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

/// <summary>
/// Conformance of the broker grant success response, checked at the JSON seam.
/// Every expected value comes from the response section of the Gateway package-management
/// contract, pinned at commit c79e7bd775370b061b5939d0ac8442c7215de869.
/// Each negative case is the valid control with exactly one named defect.
///
/// Checked clauses:
/// - Required non-null members and their exact discriminators <c>ResponseKind</c> and <c>ResponseVersion</c>.
/// - <c>Server</c>: <c>ServerVersion</c> of 1 to 128 characters and the two <c>Transport</c> values.
/// - <c>ReceivedAt</c>, <c>CompletedAt</c> and <c>Operation.SubmittedAt</c> as RFC 3339 UTC timestamps.
/// - <c>Request</c> with optional nullable <c>Manager</c>, <c>Source</c>, <c>PackageId</c> and <c>Operation</c>.
/// - <c>Authorization</c> containing only <c>Kind: RemoteGrant</c>.
/// - <c>Operation</c> with the six statuses and an optional nullable <c>EventChannel</c>
///   of <c>Kind: LocalPipe</c> and a <c>Path</c> of 1 to 1,024 characters.
/// - <c>Policy</c>, <c>Decision</c>, <c>RuleId</c> and <c>Diagnostics</c> forbidden, including as null.
/// - Unknown and duplicate members rejected at every object level.
///
/// Intentional gaps, which this suite does not decide:
/// - Grant execution, grant routing and request validation.
/// - Server authentication of the pipe.
/// - Privacy of the event channel and the semantics of <c>Source</c>.
/// - The <c>ErrorResponse</c> shape and HTTP status handling.
/// - A zero-output event stream, which is not part of the response.
/// - Any bound on <c>PackageId</c> or <c>OperationId</c>, which the contract does not state.
/// </summary>
public sealed class GrantResponseConformanceTests
{
    private const string Contract =
        "Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response";

    private static readonly string[] s_timestampPointers = ["/ReceivedAt", "/CompletedAt", "/Operation/SubmittedAt"];

    private static JsonObject Control() =>
        (JsonObject)JsonNode.Parse("""
            {
              "ResponseKind": "GrantExecutionResponse",
              "ResponseVersion": "1.0",
              "Server": { "ServerVersion": "1.2.3", "Transport": "HttpNamedPipe" },
              "RequestId": "0b3e6f0e-5d58-4a0e-9d2c-1f4f1c6c8a11",
              "ReceivedAt": "2026-11-15T05:07:42Z",
              "CompletedAt": "2026-11-15T05:07:43Z",
              "Request": {
                "Manager": "Winget",
                "Source": "winget",
                "PackageId": "Microsoft.VisualStudioCode",
                "Operation": "Install"
              },
              "Authorization": { "Kind": "RemoteGrant" },
              "Operation": {
                "OperationId": "op-7f3a",
                "Status": "Running",
                "SubmittedAt": "2026-11-15T05:07:42Z",
                "EventChannel": { "Kind": "LocalPipe", "Path": "\\\\.\\pipe\\Devolutions.Now.PackageBroker.events.op-7f3a" }
              }
            }
            """)!;

    private static JsonObject WithoutChannel()
    {
        var value = Control();
        Remove(value, "/Operation/EventChannel");
        return value;
    }

    private static (JsonObject Parent, string Key) Locate(JsonObject root, string pointer)
    {
        var segments = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parent = root;
        foreach (var segment in segments[..^1])
        {
            parent = (JsonObject)parent[segment]!;
        }

        return (parent, segments[^1]);
    }

    private static void Set(JsonObject root, string pointer, JsonNode? value)
    {
        var (parent, key) = Locate(root, pointer);
        Assert.True(parent.ContainsKey(key), $"control has no {pointer}");
        parent[key] = value;
    }

    private static void Remove(JsonObject root, string pointer)
    {
        var (parent, key) = Locate(root, pointer);
        Assert.True(parent.Remove(key), $"control has no {pointer}");
    }

    private static void Insert(JsonObject root, string parentPointer, string key, JsonNode? value)
    {
        var parent = root;
        foreach (var segment in parentPointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            parent = (JsonObject)parent[segment]!;
        }

        Assert.False(parent.ContainsKey(key), $"control already has {key}");
        parent[key] = value;
    }

    private static JsonNode? Str(string value) => JsonValue.Create(value);

    private static GrantExecutionResponse Parse(string json) =>
        BrokerSerializer.DeserializeStrict<GrantExecutionResponse>(json)
            ?? throw new JsonException("deserialized a null response");

    private static void AssertValid(string name, JsonObject value)
    {
        var json = value.ToJsonString();
        GrantExecutionResponse parsed;
        try
        {
            parsed = Parse(json);
        }
        catch (Exception error)
        {
            throw new Xunit.Sdk.XunitException($"{name}: {Contract} accepts this response: {error.Message}\n{json}");
        }

        // oracle: property round-trip
        var reserialized = JsonNode.Parse(BrokerSerializer.Serialize(parsed))!.AsObject();
        foreach (var pointer in s_timestampPointers)
        {
            var (expectedParent, key) = Locate(value, pointer);
            var (actualParent, _) = Locate(reserialized, pointer);
            Assert.Equal(
                DateTimeOffset.Parse(expectedParent[key]!.GetValue<string>(), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(actualParent[key]!.GetValue<string>(), CultureInfo.InvariantCulture));
            expectedParent.Remove(key);
            actualParent.Remove(key);
        }

        Assert.True(JsonNode.DeepEquals(value, reserialized), $"{name}: serialization must preserve the wire shape: {reserialized.ToJsonString()}");
    }

    private static void AssertRejectedAll(IEnumerable<(string Name, string Json)> cases)
    {
        var accepted = new List<string>();
        foreach (var (name, json) in cases)
        {
            try
            {
                Parse(json);
                accepted.Add(name);
            }
            catch (JsonException)
            {
            }
        }

        Assert.True(accepted.Count == 0, $"{Contract} rejects these, but they were accepted: {string.Join("; ", accepted)}");
    }

    private static IEnumerable<(string, string)> Mutated(params (string Name, Action<JsonObject> Mutate)[] cases)
    {
        foreach (var (name, mutate) in cases)
        {
            var value = Control();
            mutate(value);
            yield return (name, value.ToJsonString());
        }
    }

    [Fact]
    public void ValidResponsesAreAccepted()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        AssertValid("with event channel", Control());
        AssertValid("without event channel", WithoutChannel());

        var nullChannel = Control();
        Set(nullChannel, "/Operation/EventChannel", null);
        Parse(nullChannel.ToJsonString());

        var emptyRequest = Control();
        Set(emptyRequest, "/Request", new JsonObject());
        AssertValid("empty Request", emptyRequest);

        var nullRequestMembers = Control();
        foreach (var member in new[] { "Manager", "Source", "PackageId", "Operation" })
        {
            Set(nullRequestMembers, $"/Request/{member}", null);
        }

        Parse(nullRequestMembers.ToJsonString());
    }

    [Fact]
    public void EnumeratedValuesAreAccepted()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        foreach (var transport in new[] { "HttpNamedPipe", "HttpLoopbackSimulator" })
        {
            var value = Control();
            Set(value, "/Server/Transport", Str(transport));
            AssertValid(transport, value);
        }

        foreach (var status in new[] { "Starting", "Running", "Canceling", "Completed", "Failed", "Canceled" })
        {
            var value = Control();
            Set(value, "/Operation/Status", Str(status));
            AssertValid(status, value);
        }

        foreach (var operation in new[] { "Install", "Update", "Uninstall" })
        {
            var value = Control();
            Set(value, "/Request/Operation", Str(operation));
            AssertValid(operation, value);
        }
    }

    [Fact]
    public void FieldBoundsAreInclusive()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        foreach (var version in new[] { "1", new string('x', 128), new string('\u00e9', 128) })
        {
            var value = Control();
            Set(value, "/Server/ServerVersion", Str(version));
            AssertValid($"ServerVersion of {version.Length} characters", value);
        }

        foreach (var path in new[] { "p", new string('p', 1024), new string('\u00e9', 1024) })
        {
            var value = Control();
            Set(value, "/Operation/EventChannel/Path", Str(path));
            AssertValid($"Path of {path.Length} characters", value);
        }
    }

    [Fact]
    public void BoundsViolationsAreRejected()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        AssertRejectedAll(Mutated(
            ("empty ServerVersion", v => Set(v, "/Server/ServerVersion", Str(""))),
            ("129-character ServerVersion", v => Set(v, "/Server/ServerVersion", Str(new string('x', 129)))),
            ("129 two-byte-character ServerVersion", v => Set(v, "/Server/ServerVersion", Str(new string('\u00e9', 129)))),
            ("empty Path", v => Set(v, "/Operation/EventChannel/Path", Str(""))),
            ("1025-character Path", v => Set(v, "/Operation/EventChannel/Path", Str(new string('p', 1025))))));
    }

    [Fact]
    public void MissingAndNullRequiredMembersAreRejected()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        string[] required =
        [
            "/ResponseKind",
            "/ResponseVersion",
            "/Server",
            "/Server/ServerVersion",
            "/Server/Transport",
            "/RequestId",
            "/ReceivedAt",
            "/CompletedAt",
            "/Request",
            "/Authorization",
            "/Authorization/Kind",
            "/Operation",
            "/Operation/OperationId",
            "/Operation/Status",
            "/Operation/SubmittedAt",
            "/Operation/EventChannel/Kind",
            "/Operation/EventChannel/Path",
        ];

        var cases = new List<(string, string)>();
        foreach (var pointer in required)
        {
            var missing = Control();
            Remove(missing, pointer);
            cases.Add(($"missing {pointer}", missing.ToJsonString()));

            var nulled = Control();
            Set(nulled, pointer, null);
            cases.Add(($"null {pointer}", nulled.ToJsonString()));
        }

        AssertRejectedAll(cases);
    }

    [Fact]
    public void DiscriminatorsAndEnumerationsAreExact()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        AssertRejectedAll(Mutated(
            ("ErrorResponse kind", v => Set(v, "/ResponseKind", Str("ErrorResponse"))),
            ("lowercase kind", v => Set(v, "/ResponseKind", Str("grantexecutionresponse"))),
            ("empty kind", v => Set(v, "/ResponseKind", Str(""))),
            ("numeric kind", v => Set(v, "/ResponseKind", JsonValue.Create(1))),
            ("version 1.1", v => Set(v, "/ResponseVersion", Str("1.1"))),
            ("version 1", v => Set(v, "/ResponseVersion", Str("1"))),
            ("version 2.0", v => Set(v, "/ResponseVersion", Str("2.0"))),
            ("numeric version", v => Set(v, "/ResponseVersion", JsonValue.Create(1.0))),
            ("local authorization", v => Set(v, "/Authorization/Kind", Str("LocalPolicy"))),
            ("lowercase authorization", v => Set(v, "/Authorization/Kind", Str("remotegrant"))),
            ("empty authorization", v => Set(v, "/Authorization/Kind", Str(""))),
            ("unknown transport", v => Set(v, "/Server/Transport", Str("Http"))),
            ("lowercase transport", v => Set(v, "/Server/Transport", Str("httpnamedpipe"))),
            ("unknown status", v => Set(v, "/Operation/Status", Str("Pending"))),
            ("lowercase status", v => Set(v, "/Operation/Status", Str("running"))),
            ("numeric status", v => Set(v, "/Operation/Status", JsonValue.Create(1))),
            ("unknown request operation", v => Set(v, "/Request/Operation", Str("Remove"))),
            ("lowercase request operation", v => Set(v, "/Request/Operation", Str("install"))),
            ("unknown manager", v => Set(v, "/Request/Manager", Str("NotAManager"))),
            ("unknown channel kind", v => Set(v, "/Operation/EventChannel/Kind", Str("NamedPipe"))),
            ("lowercase channel kind", v => Set(v, "/Operation/EventChannel/Kind", Str("localpipe")))));
    }

    [Fact]
    public void InvalidNestedValuesAreRejected()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        AssertRejectedAll(Mutated(
            ("numeric ServerVersion", v => Set(v, "/Server/ServerVersion", JsonValue.Create(1))),
            ("numeric RequestId", v => Set(v, "/RequestId", JsonValue.Create(1))),
            // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §operations
            // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
            ("non-UUID RequestId", v => Set(v, "/RequestId", Str("not-a-uuid"))),
            ("empty RequestId", v => Set(v, "/RequestId", Str(""))),
            ("numeric OperationId", v => Set(v, "/Operation/OperationId", JsonValue.Create(1))),
            ("numeric Source", v => Set(v, "/Request/Source", JsonValue.Create(1))),
            ("numeric PackageId", v => Set(v, "/Request/PackageId", JsonValue.Create(1))),
            ("numeric Path", v => Set(v, "/Operation/EventChannel/Path", JsonValue.Create(1))),
            ("array Request", v => Set(v, "/Request", new JsonArray())),
            ("string Server", v => Set(v, "/Server", Str("HttpNamedPipe"))),
            ("string Authorization", v => Set(v, "/Authorization", Str("RemoteGrant"))),
            ("string EventChannel", v => Set(v, "/Operation/EventChannel", Str("LocalPipe"))),
            ("array Operation", v => Set(v, "/Operation", new JsonArray()))));
    }

    [Fact]
    public void TimestampsMustBeRfc3339Utc()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        (string Name, Func<JsonNode?> Bad)[] invalid =
        [
            ("word", () => Str("yesterday")),
            ("empty", () => Str("")),
            ("number", () => JsonValue.Create(1_763_182_062)),
            ("space separator", () => Str("2026-11-15 05:07:42Z")),
            ("no offset", () => Str("2026-11-15T05:07:42")),
            ("non-UTC offset", () => Str("2026-11-15T14:07:42+09:00")),
            ("impossible date", () => Str("2026-02-30T05:07:42Z")),
        ];

        var cases = new List<(string, string)>();
        foreach (var pointer in s_timestampPointers)
        {
            foreach (var (name, bad) in invalid)
            {
                var value = Control();
                Set(value, pointer, bad());
                cases.Add(($"{name} {pointer}", value.ToJsonString()));
            }
        }

        AssertRejectedAll(cases);
    }

    [Fact]
    public void ForbiddenMembersAreRejectedEvenAsNull()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        var cases = new List<(string, string)>();
        foreach (var member in new[] { "Policy", "Decision", "RuleId", "Diagnostics" })
        {
            foreach (var (shape, forbidden) in new (string, Func<JsonNode?>)[]
            {
                ("null", () => null),
                ("value", () => Str("Allow")),
                ("object", () => new JsonObject()),
            })
            {
                foreach (var (variant, build) in new (string, Func<JsonObject>)[]
                {
                    ("with channel", Control),
                    ("without channel", WithoutChannel),
                })
                {
                    var value = build();
                    Insert(value, "", member, forbidden());
                    cases.Add(($"{shape} {member} {variant}", value.ToJsonString()));
                }
            }
        }

        AssertRejectedAll(cases);
    }

    [Fact]
    public void UnknownMembersAreRejectedAtEveryObjectLevel()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        (string Name, Func<JsonNode?> Extra)[] extras =
        [
            ("Extra", () => JsonValue.Create(true)),
            ("Grant", () => Str("secret")),
            ("Stdout", () => Str("")),
            ("CommandPreview", () => null),
            ("kind", () => Str("RemoteGrant")),
        ];

        var cases = new List<(string, string)>();
        foreach (var level in new[] { "", "/Server", "/Request", "/Authorization", "/Operation", "/Operation/EventChannel" })
        {
            foreach (var (name, extra) in extras)
            {
                var value = Control();
                Insert(value, level, name, extra());
                cases.Add(($"{name} at '{level}'", value.ToJsonString()));
            }
        }

        AssertRejectedAll(cases);
    }

    [Fact]
    public void DuplicateMembersAreRejectedAtEveryObjectLevel()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        var controlText = Control().ToJsonString();
        var cases = new List<(string, string)>();
        foreach (var (level, fragment) in new[]
        {
            ("top", "\"ResponseVersion\":\"1.0\""),
            ("Server", "\"Transport\":\"HttpNamedPipe\""),
            ("Request", "\"Operation\":\"Install\""),
            ("Authorization", "\"Kind\":\"RemoteGrant\""),
            ("Operation", "\"Status\":\"Running\""),
            ("EventChannel", "\"Kind\":\"LocalPipe\""),
        })
        {
            var index = controlText.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(index >= 0 && controlText.IndexOf(fragment, index + 1, StringComparison.Ordinal) < 0, $"{fragment} must anchor exactly one member");
            cases.Add(($"duplicate in {level}", controlText.Insert(index, fragment + ",")));
        }

        AssertRejectedAll(cases);
    }

    [Fact]
    public void NonObjectAndMalformedDocumentsAreRejected()
    {
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        var valid = Control().ToJsonString();
        AssertRejectedAll(
        [
            ("array", "[]"),
            ("string", "\"GrantExecutionResponse\""),
            ("null", "null"),
            ("empty", ""),
            ("truncated", valid[..^1]),
            ("trailing content", valid + " {}"),
        ]);
    }
}