using System.Text;
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

    public static string Serialize(PolicyDocument value)
    {
        ValidateRequiredCollectionElements(value);
        return JsonSerializer.Serialize(value, PolicyJsonSerializerContext.Default.PolicyDocument);
    }

    public static string Serialize(PolicyDraftDocument value)
    {
        ValidateRequiredCollectionElements(value);
        return JsonSerializer.Serialize(value, PolicyJsonSerializerContext.Default.PolicyDraftDocument);
    }

    public static PolicyDocument? DeserializePolicyDocument(string json) =>
        Validate(JsonSerializer.Deserialize(json, PolicyJsonSerializerContext.Default.PolicyDocument));

    public static PolicyDocument? DeserializePolicyDocumentStrict(string json) =>
        Validate(JsonSerializer.Deserialize(json, PolicyJsonStrictSerializerContext.Default.PolicyDocument));

    public static PolicyDraftDocument? DeserializePolicyDraftDocumentStrict(string json) =>
        Validate(JsonSerializer.Deserialize(json, PolicyJsonStrictSerializerContext.Default.PolicyDraftDocument));

    public static string Serialize<T>(T value)
    {
        ValidateSemanticValue(value);
        return JsonSerializer.Serialize(value, TypeInfo<T>());
    }

    public static T? DeserializeStrict<T>(string json)
    {
        var value = JsonSerializer.Deserialize(json, StrictTypeInfo<T>());
        ValidateSemanticValue(value);
        return value;
    }

    private static void ValidateSemanticValue<T>(T value)
    {
        switch (value)
        {
            case PolicyDocument policy:
                ValidateRequiredCollectionElements(policy);
                break;
            case PolicyDraftDocument draft:
                ValidateRequiredCollectionElements(draft);
                break;
            case PolicyRule rule:
                ValidateRequiredCollectionElements(rule, "$");
                break;
            case PolicyMatch match:
                ValidateRequiredCollectionElements(match, "$");
                break;
        }
    }

    internal static void ValidateRequiredCollectionElements(PolicyDocument policy)
        => ValidateRequiredCollectionElements(policy.Rules);

    internal static void ValidateRequiredCollectionElements(PolicyDraftDocument policy)
        => ValidateRequiredCollectionElements(policy.Rules);

    private static void ValidateRequiredCollectionElements(IReadOnlyList<PolicyRule> rules)
    {
        RejectNullElements(rules, "$.Rules");

        for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            ValidateRequiredCollectionElements(rules[ruleIndex], $"$.Rules[{ruleIndex}]");
        }
    }

    private static void ValidateRequiredCollectionElements(PolicyRule rule, string path)
    {
        ValidateRequiredCollectionElements(rule.Match, $"{path}.Match");

        if (rule.Constraints is { } constraints)
        {
            var constraintsPath = $"{path}.Constraints";
            RejectBoundedStrings(
                constraints.AllowedInstallLocationPatterns,
                1,
                256,
                $"{constraintsPath}.AllowedInstallLocationPatterns");
            RejectBoundedStrings(
                constraints.AllowedCustomParameters,
                1,
                512,
                $"{constraintsPath}.AllowedCustomParameters");
            RejectBoundedStrings(
                constraints.AllowedCustomParameterPatterns,
                1,
                512,
                $"{constraintsPath}.AllowedCustomParameterPatterns");
            RejectBoundedStrings(
                constraints.DeniedCustomParameters,
                1,
                512,
                $"{constraintsPath}.DeniedCustomParameters");
        }
    }

    private static void ValidateRequiredCollectionElements(PolicyMatch match, string path)
    {
        RejectBoundedStrings(match.Sources, 1, 256, $"{path}.Sources");
        RejectBoundedStrings(match.PackageIdentifiers, 1, 256, $"{path}.PackageIdentifiers");
        RejectBoundedStrings(match.PackageNames, 1, 256, $"{path}.PackageNames");
        RejectBoundedStrings(match.Versions, 1, 128, $"{path}.Versions");
        RejectBooleanMatch(match.Interactive, $"{path}.Interactive");
        RejectBooleanMatch(match.SkipHashCheck, $"{path}.SkipHashCheck");
        RejectBooleanMatch(match.PreRelease, $"{path}.PreRelease");
        RejectBooleanMatch(match.HasCustomParameters, $"{path}.HasCustomParameters");
        RejectBooleanMatch(match.HasCustomInstallLocation, $"{path}.HasCustomInstallLocation");
        RejectBooleanMatch(match.HasPrePostCommands, $"{path}.HasPrePostCommands");
        RejectBooleanMatch(match.HasKillBeforeOperation, $"{path}.HasKillBeforeOperation");
        RejectBooleanMatch(match.HasUninstallPrevious, $"{path}.HasUninstallPrevious");
    }

    private static PolicyDocument? Validate(PolicyDocument? policy)
    {
        if (policy is not null)
        {
            ValidateRequiredCollectionElements(policy);
        }

        return policy;
    }

    private static PolicyDraftDocument? Validate(PolicyDraftDocument? policy)
    {
        if (policy is not null)
        {
            ValidateRequiredCollectionElements(policy);
        }

        return policy;
    }

    private static void RejectBooleanMatch(IReadOnlyList<bool> values, string path)
    {
        if (values.Count > 1)
        {
            throw new JsonException($"The JSON array at {path} must contain at most one value.");
        }
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

    private static void RejectBoundedStrings(
        IReadOnlyList<string> values,
        int minLength,
        int maxLength,
        string path)
    {
        RejectNullElements(values, path);
        for (var index = 0; index < values.Count; index++)
        {
            var length = values[index].EnumerateRunes().Count();
            if (length < minLength || length > maxLength)
            {
                throw new JsonException(
                    $"The JSON string at {path}[{index}] must contain between {minLength} and {maxLength} Unicode scalar values; found {length}.");
            }
        }
    }

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        typeof(T) == typeof(PolicyDocument) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyDocument) :
        typeof(T) == typeof(PolicyDraftDocument) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyDraftDocument) :
        typeof(T) == typeof(PolicyMetadata) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyMetadata) :
        typeof(T) == typeof(PolicyDraftMetadata) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyDraftMetadata) :
        typeof(T) == typeof(PolicyEnforcement) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyEnforcement) :
        typeof(T) == typeof(PolicyRule) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyRule) :
        typeof(T) == typeof(PolicyMatch) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyMatch) :
        typeof(T) == typeof(VersionRange) ? Cast<T>(PolicyJsonSerializerContext.Default.VersionRange) :
        typeof(T) == typeof(PolicyConstraints) ? Cast<T>(PolicyJsonSerializerContext.Default.PolicyConstraints) :
        throw new NotSupportedException($"Policy JSON serialization for {typeof(T).FullName} is not source-generated.");

    private static JsonTypeInfo<T> StrictTypeInfo<T>() =>
        typeof(T) == typeof(PolicyDocument) ? Cast<T>(PolicyJsonStrictSerializerContext.Default.PolicyDocument) :
        typeof(T) == typeof(PolicyDraftDocument) ? Cast<T>(PolicyJsonStrictSerializerContext.Default.PolicyDraftDocument) :
        typeof(T) == typeof(PolicyMetadata) ? Cast<T>(PolicyJsonStrictSerializerContext.Default.PolicyMetadata) :
        typeof(T) == typeof(PolicyDraftMetadata) ? Cast<T>(PolicyJsonStrictSerializerContext.Default.PolicyDraftMetadata) :
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
[JsonSerializable(typeof(PolicyDraftDocument))]
[JsonSerializable(typeof(PolicyMetadata))]
[JsonSerializable(typeof(PolicyDraftMetadata))]
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
[JsonSerializable(typeof(PolicyDraftDocument))]
[JsonSerializable(typeof(PolicyMetadata))]
[JsonSerializable(typeof(PolicyDraftMetadata))]
[JsonSerializable(typeof(PolicyEnforcement))]
[JsonSerializable(typeof(PolicyRule))]
[JsonSerializable(typeof(PolicyMatch))]
[JsonSerializable(typeof(VersionRange))]
[JsonSerializable(typeof(PolicyConstraints))]
internal sealed partial class PolicyJsonStrictSerializerContext : JsonSerializerContext;