using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Devolutions.Now.Policy.Api;

/// <summary>Canonical schema URI used in the <c>$schema</c> field of policy documents.</summary>
public static class SchemaUris
{
    public const string Policy = "https://devolutions.net/schemas/now-policy.schema.1.0.json";
}

/// <summary>Shared <see cref="JsonSerializerOptions"/> for broker documents.</summary>
public static class BrokerJson
{
    /// <summary>
    /// Serialization options matching the broker wire format: PascalCase property names
    /// (via explicit <c>[JsonPropertyName]</c> attributes), PascalCase enum values, and
    /// null optionals omitted (mirroring the Rust <c>skip_serializing_if = "Option::is_none"</c>).
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(BrokerJsonSerializerContext.Default.Options)
    {
    };

    public static readonly JsonSerializerOptions PrettyOptions = new(Options)
    {
        WriteIndented = true,
    };

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, TypeInfo<T>());

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize(json, TypeInfo<T>());

    public static T? DeserializeStrict<T>(string json) =>
        JsonSerializer.Deserialize(json, StrictTypeInfo<T>());

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        typeof(T) == typeof(PackageRequest) ? Cast<T>(BrokerJsonSerializerContext.Default.PackageRequest) :
        typeof(T) == typeof(StatusRequest) ? Cast<T>(BrokerJsonSerializerContext.Default.StatusRequest) :
        typeof(T) == typeof(HealthResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.HealthResponse) :
        typeof(T) == typeof(CapabilitiesResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.CapabilitiesResponse) :
        typeof(T) == typeof(EvaluationResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.EvaluationResponse) :
        typeof(T) == typeof(ExecutionResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.ExecutionResponse) :
        typeof(T) == typeof(StatusResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.StatusResponse) :
        typeof(T) == typeof(ErrorResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.ErrorResponse) :
        throw new NotSupportedException($"Broker JSON serialization for {typeof(T).FullName} is not source-generated.");

    private static JsonTypeInfo<T> StrictTypeInfo<T>() =>
        typeof(T) == typeof(PackageRequest) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.PackageRequest) :
        typeof(T) == typeof(StatusRequest) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.StatusRequest) :
        typeof(T) == typeof(HealthResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.HealthResponse) :
        typeof(T) == typeof(CapabilitiesResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.CapabilitiesResponse) :
        typeof(T) == typeof(EvaluationResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.EvaluationResponse) :
        typeof(T) == typeof(ExecutionResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.ExecutionResponse) :
        typeof(T) == typeof(StatusResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.StatusResponse) :
        typeof(T) == typeof(ErrorResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.ErrorResponse) :
        throw new NotSupportedException($"Strict broker JSON deserialization for {typeof(T).FullName} is not source-generated.");

    private static JsonTypeInfo<T> Cast<T>(JsonTypeInfo jsonTypeInfo) =>
        (JsonTypeInfo<T>)jsonTypeInfo;
}

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(PackageRequest))]
[JsonSerializable(typeof(StatusRequest))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(CapabilitiesResponse))]
[JsonSerializable(typeof(EvaluationResponse))]
[JsonSerializable(typeof(ExecutionResponse))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
internal sealed partial class BrokerJsonSerializerContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PackageRequest))]
[JsonSerializable(typeof(StatusRequest))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(CapabilitiesResponse))]
[JsonSerializable(typeof(EvaluationResponse))]
[JsonSerializable(typeof(ExecutionResponse))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
internal sealed partial class BrokerJsonStrictSerializerContext : JsonSerializerContext;