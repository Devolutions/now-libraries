using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Devolutions.Now.Policy.Model;

public static class PolicyJson
{
    public static readonly JsonSerializerOptions Options = new(PolicyJsonSerializerContext.Default.Options)
    {
    };

    public static readonly JsonSerializerOptions StrictOptions = new(Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Serialize(PolicyDocument value) =>
        JsonSerializer.Serialize(value, PolicyJsonSerializerContext.Default.PolicyDocument);

    public static PolicyDocument? DeserializePolicyDocument(string json) =>
        Validate(JsonSerializer.Deserialize(json, PolicyJsonSerializerContext.Default.PolicyDocument));

    public static PolicyDocument? DeserializePolicyDocumentStrict(string json) =>
        Validate(JsonSerializer.Deserialize(json, PolicyJsonStrictSerializerContext.Default.PolicyDocument));

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, TypeInfo<T>());

    public static T? DeserializeStrict<T>(string json)
    {
        var value = JsonSerializer.Deserialize(json, StrictTypeInfo<T>());
        if (value is PolicyDocument policy)
        {
            ValidateRequiredCollectionElements(policy);
        }

        return value;
    }

    internal static void ValidateRequiredCollectionElements(PolicyDocument policy)
    {
        RejectNullElements(policy.Rules, "$.Rules");

        for (var ruleIndex = 0; ruleIndex < policy.Rules.Count; ruleIndex++)
        {
            var rule = policy.Rules[ruleIndex];
            var matchPath = $"$.Rules[{ruleIndex}].Match";
            RejectNullElements(rule.Match.Sources, $"{matchPath}.Sources");
            RejectNullElements(rule.Match.PackageIdentifiers, $"{matchPath}.PackageIdentifiers");
            RejectNullElements(rule.Match.PackageNames, $"{matchPath}.PackageNames");
            RejectNullElements(rule.Match.Versions, $"{matchPath}.Versions");

            if (rule.Constraints is { } constraints)
            {
                var constraintsPath = $"$.Rules[{ruleIndex}].Constraints";
                RejectNullElements(
                    constraints.AllowedInstallLocationPatterns,
                    $"{constraintsPath}.AllowedInstallLocationPatterns");
                RejectNullElements(constraints.AllowedCustomParameters, $"{constraintsPath}.AllowedCustomParameters");
                RejectNullElements(
                    constraints.AllowedCustomParameterPatterns,
                    $"{constraintsPath}.AllowedCustomParameterPatterns");
                RejectNullElements(constraints.DeniedCustomParameters, $"{constraintsPath}.DeniedCustomParameters");
            }
        }
    }

    private static PolicyDocument? Validate(PolicyDocument? policy)
    {
        if (policy is not null)
        {
            ValidateRequiredCollectionElements(policy);
        }

        return policy;
    }

    private static void RejectNullElements<T>(IReadOnlyList<T> values, string path)
        where T : class
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is null)
            {
                throw new JsonException($"The JSON value at {path}[{index}] must not be null.");
            }
        }
    }

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        typeof(T) == typeof(PolicyDocument) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyDocument) :
        typeof(T) == typeof(PolicyMetadata) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyMetadata) :
        typeof(T) == typeof(PolicyEnforcement) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyEnforcement) :
        typeof(T) == typeof(PolicyRule) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyRule) :
        typeof(T) == typeof(PolicyMatch) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyMatch) :
        typeof(T) == typeof(VersionRange) ? Cast<T>(PolicyJsonSerializerContext.Default.VersionRange) :
        typeof(T) == typeof(PolicyConstraints) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyConstraints) :
        throw new NotSupportedException($"Policy JSON serialization for {typeof(T).FullName} is not source-generated.");

    private static JsonTypeInfo<T> StrictTypeInfo<T>() =>
        typeof(T) == typeof(PolicyDocument) ? Cast<T>(PolicyJsonStrictSerializerContext.Default.PolicyDocument) :
        typeof(T) == typeof(PolicyMetadata) ? Cast<T>(PolicyJsonStrictSerializerContext.Default.PolicyMetadata) :
        typeof(T) == typeof(PolicyEnforcement) ? Cast<T>(PolicyJsonStrictSerializerContext.Default.PolicyEnforcement) :
        typeof(T) == typeof(PolicyRule) ? Cast<T>(PolicyJsonStrictSerializerContext.Default.PolicyRule) :
        typeof(T) == typeof(PolicyMatch) ? Cast<T>(PolicyJsonStrictSerializerContext.Default.PolicyMatch) :
        typeof(T) == typeof(VersionRange) ? Cast<T>(PolicyJsonStrictSerializerContext.Default.VersionRange) :
        typeof(T) == typeof(PolicyConstraints) ? Cast<T>(PolicyJsonStrictSerializerContext.Default.PolicyConstraints) :
        throw new NotSupportedException($"Strict policy JSON deserialization for {typeof(T).FullName} is not source-generated.");

    private static JsonTypeInfo<T> Cast<T>(JsonTypeInfo jsonTypeInfo) =>
        (JsonTypeInfo<T>)jsonTypeInfo;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(PolicyDocument))]
[JsonSerializable(typeof(PolicyMetadata))]
[JsonSerializable(typeof(PolicyEnforcement))]
[JsonSerializable(typeof(PolicyRule))]
[JsonSerializable(typeof(PolicyMatch))]
[JsonSerializable(typeof(VersionRange))]
[JsonSerializable(typeof(PolicyConstraints))]
internal sealed partial class PolicyJsonSerializerContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PolicyDocument))]
[JsonSerializable(typeof(PolicyMetadata))]
[JsonSerializable(typeof(PolicyEnforcement))]
[JsonSerializable(typeof(PolicyRule))]
[JsonSerializable(typeof(PolicyMatch))]
[JsonSerializable(typeof(VersionRange))]
[JsonSerializable(typeof(PolicyConstraints))]
internal sealed partial class PolicyJsonStrictSerializerContext : JsonSerializerContext;