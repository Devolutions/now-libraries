using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Devolutions.Now.Policy.Model;

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
    public static readonly JsonSerializerOptions Options = CreateOptions(writeIndented: false);

    public static readonly JsonSerializerOptions PrettyOptions = CreateOptions(writeIndented: true);

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, TypeInfo<T>());

    public static T? Deserialize<T>(string json)
    {
        var value = JsonSerializer.Deserialize(json, TypeInfo<T>());
        if (value is ErrorResponse { Validation: { } validation })
        {
            ValidateValidation(validation);
        }

        return value;
    }

    public static T? DeserializeStrict<T>(string json)
    {
        var value = JsonSerializer.Deserialize(json, StrictTypeInfo<T>());
        switch (value)
        {
            case PolicyResponse response:
                PolicyJson.ValidateRequiredCollectionElements(response.Policy);
                break;
            case PolicyManagementResponse response:
                ValidateManagement(response.Management);
                break;
            case PolicyValidationResponse response:
                ValidateValidation(response.Validation);
                break;
            case PolicyReplacementResponse response:
                PolicyJson.ValidateRequiredCollectionElements(response.Policy);
                ValidateValidation(response.Validation);
                ValidateManagement(response.Management);
                break;
            case ErrorResponse { Validation: { } validation }:
                ValidateValidation(validation);
                break;
        }

        return value;
    }

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        typeof(T) == typeof(PackageRequest) ? Cast<T>(BrokerJsonSerializerContext.Default.PackageRequest) :
        typeof(T) == typeof(StatusRequest) ? Cast<T>(BrokerJsonSerializerContext.Default.StatusRequest) :
        typeof(T) == typeof(CancelRequest) ? Cast<T>(BrokerJsonSerializerContext.Default.CancelRequest) :
        typeof(T) == typeof(PolicyValidationRequest) ? Cast<T>(BrokerPolicyJsonSerializerContext.Default.PolicyValidationRequest) :
        typeof(T) == typeof(PolicyReplacementRequest) ? Cast<T>(BrokerPolicyJsonSerializerContext.Default.PolicyReplacementRequest) :
        typeof(T) == typeof(HealthResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.HealthResponse) :
        typeof(T) == typeof(CapabilitiesResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.CapabilitiesResponse) :
        typeof(T) == typeof(PolicyResponse) ? Cast<T>(BrokerPolicyJsonSerializerContext.Default.PolicyResponse) :
        typeof(T) == typeof(PolicyManagementResponse) ? Cast<T>(BrokerPolicyJsonSerializerContext.Default.PolicyManagementResponse) :
        typeof(T) == typeof(PolicyValidationResponse) ? Cast<T>(BrokerPolicyJsonSerializerContext.Default.PolicyValidationResponse) :
        typeof(T) == typeof(PolicyReplacementResponse) ? Cast<T>(BrokerPolicyJsonSerializerContext.Default.PolicyReplacementResponse) :
        typeof(T) == typeof(EvaluationResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.EvaluationResponse) :
        typeof(T) == typeof(ExecutionResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.ExecutionResponse) :
        typeof(T) == typeof(StatusResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.StatusResponse) :
        typeof(T) == typeof(CancelResponse) ? Cast<T>(BrokerJsonSerializerContext.Default.CancelResponse) :
        typeof(T) == typeof(ErrorResponse) ? Cast<T>(BrokerErrorJsonSerializerContext.Default.ErrorResponse) :
        throw new NotSupportedException($"Broker JSON serialization for {typeof(T).FullName} is not source-generated.");

    private static JsonTypeInfo<T> StrictTypeInfo<T>() =>
        typeof(T) == typeof(PackageRequest) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.PackageRequest) :
        typeof(T) == typeof(StatusRequest) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.StatusRequest) :
        typeof(T) == typeof(CancelRequest) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.CancelRequest) :
        typeof(T) == typeof(PolicyValidationRequest) ? Cast<T>(BrokerPolicyJsonStrictSerializerContext.Default.PolicyValidationRequest) :
        typeof(T) == typeof(PolicyReplacementRequest) ? Cast<T>(BrokerPolicyJsonStrictSerializerContext.Default.PolicyReplacementRequest) :
        typeof(T) == typeof(HealthResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.HealthResponse) :
        typeof(T) == typeof(CapabilitiesResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.CapabilitiesResponse) :
        typeof(T) == typeof(PolicyResponse) ? Cast<T>(BrokerPolicyJsonStrictSerializerContext.Default.PolicyResponse) :
        typeof(T) == typeof(PolicyManagementResponse) ? Cast<T>(BrokerPolicyJsonStrictSerializerContext.Default.PolicyManagementResponse) :
        typeof(T) == typeof(PolicyValidationResponse) ? Cast<T>(BrokerPolicyJsonStrictSerializerContext.Default.PolicyValidationResponse) :
        typeof(T) == typeof(PolicyReplacementResponse) ? Cast<T>(BrokerPolicyJsonStrictSerializerContext.Default.PolicyReplacementResponse) :
        typeof(T) == typeof(EvaluationResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.EvaluationResponse) :
        typeof(T) == typeof(ExecutionResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.ExecutionResponse) :
        typeof(T) == typeof(StatusResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.StatusResponse) :
        typeof(T) == typeof(CancelResponse) ? Cast<T>(BrokerJsonStrictSerializerContext.Default.CancelResponse) :
        typeof(T) == typeof(ErrorResponse) ? Cast<T>(BrokerErrorJsonStrictSerializerContext.Default.ErrorResponse) :
        throw new NotSupportedException($"Strict broker JSON deserialization for {typeof(T).FullName} is not source-generated.");

    private static JsonTypeInfo<T> Cast<T>(JsonTypeInfo jsonTypeInfo) =>
        (JsonTypeInfo<T>)jsonTypeInfo;

    private static void ValidateManagement(PolicyManagementSnapshot management)
    {
        if (management.Policy is { } policy)
        {
            PolicyJson.ValidateRequiredCollectionElements(policy);
        }
    }

    private static void ValidateValidation(PolicyValidationResult validation)
    {
        if (validation.IsValid)
        {
            if (validation.CanonicalDraft is null || validation.ValidationReceipt is null)
            {
                throw new JsonException(
                    "Valid policy validation results require CanonicalDraft and ValidationReceipt.");
            }

            PolicyJson.ValidateRequiredCollectionElements(validation.CanonicalDraft);
        }
        else if (validation.CanonicalDraft is not null || validation.ValidationReceipt is not null)
        {
            throw new JsonException(
                "Invalid policy validation results must not contain CanonicalDraft or ValidationReceipt.");
        }
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented) =>
        new(BrokerJsonSerializerContext.Default.Options)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                BrokerJsonSerializerContext.Default,
                BrokerPolicyJsonSerializerContext.Default,
                BrokerErrorJsonSerializerContext.Default),
            WriteIndented = writeIndented,
        };
}

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    WriteIndented = false)]
[JsonSerializable(typeof(PackageRequest))]
[JsonSerializable(typeof(StatusRequest))]
[JsonSerializable(typeof(CancelRequest))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(CapabilitiesResponse))]
[JsonSerializable(typeof(EvaluationResponse))]
[JsonSerializable(typeof(ExecutionResponse))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(CancelResponse))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
internal sealed partial class BrokerJsonSerializerContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    WriteIndented = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PackageRequest))]
[JsonSerializable(typeof(StatusRequest))]
[JsonSerializable(typeof(CancelRequest))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(CapabilitiesResponse))]
[JsonSerializable(typeof(EvaluationResponse))]
[JsonSerializable(typeof(ExecutionResponse))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(CancelResponse))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
internal sealed partial class BrokerJsonStrictSerializerContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    Converters = new[] { typeof(ExactCaseTransportConverter) },
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    WriteIndented = false)]
[JsonSerializable(typeof(PolicyResponse))]
[JsonSerializable(typeof(PolicyManagementResponse))]
[JsonSerializable(typeof(PolicyValidationRequest))]
[JsonSerializable(typeof(PolicyValidationResponse))]
[JsonSerializable(typeof(PolicyReplacementRequest))]
[JsonSerializable(typeof(PolicyReplacementResponse))]
internal sealed partial class BrokerPolicyJsonSerializerContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    Converters = new[] { typeof(ExactCaseTransportConverter) },
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    WriteIndented = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PolicyResponse))]
[JsonSerializable(typeof(PolicyManagementResponse))]
[JsonSerializable(typeof(PolicyValidationRequest))]
[JsonSerializable(typeof(PolicyValidationResponse))]
[JsonSerializable(typeof(PolicyReplacementRequest))]
[JsonSerializable(typeof(PolicyReplacementResponse))]
internal sealed partial class BrokerPolicyJsonStrictSerializerContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    WriteIndented = false)]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class BrokerErrorJsonSerializerContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    WriteIndented = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class BrokerErrorJsonStrictSerializerContext : JsonSerializerContext;