using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Devolutions.Now.Policy.Model;

public static class PolicySerializer
{
    public static readonly JsonSerializerOptions Options = CreateOptions(PolicySerializerContext.Default);

    public static readonly JsonSerializerOptions StrictOptions = CreateOptions(PolicyStrictSerializerContext.Default);

    public static string Serialize(PolicyDocument value)
    {
        ValidateRequiredCollectionElements(value);
        return JsonSerializer.Serialize(value, PolicySerializerContext.Default.PolicyDocument);
    }

    public static string Serialize(PolicyDraftDocument value)
    {
        ValidateRequiredCollectionElements(value);
        return JsonSerializer.Serialize(value, PolicySerializerContext.Default.PolicyDraftDocument);
    }

    public static PolicyDocument? DeserializePolicyDocument(string json) =>
        Validate(JsonSerializer.Deserialize(json, PolicySerializerContext.Default.PolicyDocument));

    public static PolicyDocument? DeserializePolicyDocumentStrict(string json) =>
        Validate(JsonSerializer.Deserialize(json, PolicyStrictSerializerContext.Default.PolicyDocument));

    public static PolicyDraftDocument? DeserializePolicyDraftDocumentStrict(string json) =>
        Validate(JsonSerializer.Deserialize(json, PolicyStrictSerializerContext.Default.PolicyDraftDocument));

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

    private static void ValidateSemanticValue(object? value)
    {
        switch (value)
        {
            case PolicyDocument policy:
                ValidateRequiredCollectionElements(policy);
                break;
            case PolicyMetadata metadata:
                ValidatePolicyRevision(metadata.Revision);
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
    {
        ValidatePolicyRevision(policy.Metadata.Revision);
        ValidateRequiredCollectionElements(policy.Rules);
    }

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

    private static void ValidatePolicyRevision(uint revision)
    {
        if (revision is 0 or > int.MaxValue)
        {
            throw new JsonException($"Policy revision must be between 1 and {int.MaxValue}.");
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
        typeof(T) == typeof(PolicyDocument) ? Cast<T>(PolicySerializerContext.Default.PolicyDocument) :
        typeof(T) == typeof(PolicyDraftDocument) ? Cast<T>(PolicySerializerContext.Default.PolicyDraftDocument) :
        typeof(T) == typeof(PolicyMetadata) ? Cast<T>(PolicySerializerContext.Default.PolicyMetadata) :
        typeof(T) == typeof(PolicyDraftMetadata) ? Cast<T>(PolicySerializerContext.Default.PolicyDraftMetadata) :
        typeof(T) == typeof(PolicyEnforcement) ? Cast<T>(PolicySerializerContext.Default.PolicyEnforcement) :
        typeof(T) == typeof(PolicyRule) ? Cast<T>(PolicySerializerContext.Default.PolicyRule) :
        typeof(T) == typeof(PolicyMatch) ? Cast<T>(PolicySerializerContext.Default.PolicyMatch) :
        typeof(T) == typeof(VersionRange) ? Cast<T>(PolicySerializerContext.Default.VersionRange) :
        typeof(T) == typeof(PolicyConstraints) ? Cast<T>(PolicySerializerContext.Default.PolicyConstraints) :
        throw new NotSupportedException($"Policy JSON serialization for {typeof(T).FullName} is not source-generated.");

    private static JsonTypeInfo<T> StrictTypeInfo<T>() =>
        typeof(T) == typeof(PolicyDocument) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyDocument) :
        typeof(T) == typeof(PolicyDraftDocument) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyDraftDocument) :
        typeof(T) == typeof(PolicyMetadata) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyMetadata) :
        typeof(T) == typeof(PolicyDraftMetadata) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyDraftMetadata) :
        typeof(T) == typeof(PolicyEnforcement) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyEnforcement) :
        typeof(T) == typeof(PolicyRule) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyRule) :
        typeof(T) == typeof(PolicyMatch) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyMatch) :
        typeof(T) == typeof(VersionRange) ? Cast<T>(PolicyStrictSerializerContext.Default.VersionRange) :
        typeof(T) == typeof(PolicyConstraints) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyConstraints) :
        throw new NotSupportedException($"Strict policy JSON deserialization for {typeof(T).FullName} is not source-generated.");

    private static JsonTypeInfo<T> Cast<T>(JsonTypeInfo jsonTypeInfo) =>
        (JsonTypeInfo<T>)jsonTypeInfo;

    private static JsonSerializerOptions CreateOptions(JsonSerializerContext context) =>
        new(context.Options)
        {
            TypeInfoResolver = context.WithAddedModifier(AttachSemanticValidation),
        };

    private static void AttachSemanticValidation(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        typeInfo.OnSerializing = ValidateSemanticValue;
        typeInfo.OnDeserialized = ValidateSemanticValue;
    }
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
internal sealed partial class PolicySerializerContext : JsonSerializerContext;

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
internal sealed partial class PolicyStrictSerializerContext : JsonSerializerContext;