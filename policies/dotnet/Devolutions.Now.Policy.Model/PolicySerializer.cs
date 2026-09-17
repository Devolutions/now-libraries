using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Devolutions.Now.Policy.Model;

public static partial class PolicySerializer
{
    /// <summary>
    /// Source-generated policy JSON options. Deserialization rejects duplicate property names
    /// throughout the input using ordinal, case-sensitive name comparison.
    /// </summary>
    public static readonly JsonSerializerOptions Options = CreateOptions(strict: false);

    /// <summary>
    /// Source-generated strict policy JSON options. Deserialization rejects unknown and duplicate
    /// property names throughout the input using ordinal, case-sensitive name comparison.
    /// </summary>
    public static readonly JsonSerializerOptions StrictOptions = CreateOptions(strict: true);

    public static string Serialize(PolicyDocument value)
    {
        ValidateRequiredCollectionElements(value);
        return JsonSerializer.Serialize(value, TypeInfo<PolicyDocument>());
    }

    public static string Serialize(PolicyDraftDocument value)
    {
        ValidateRequiredCollectionElements(value);
        return JsonSerializer.Serialize(value, TypeInfo<PolicyDraftDocument>());
    }

    public static PolicyDocument? DeserializePolicyDocument(string json)
    {
        PolicyJsonInput.RejectDuplicatePropertyNames(json, PolicySerializerContext.Default.Options);
        return Validate(JsonSerializer.Deserialize(json, PolicySerializerContext.Default.PolicyDocument));
    }

    public static PolicyDocument? DeserializePolicyDocumentStrict(string json)
    {
        PolicyJsonInput.RejectDuplicatePropertyNames(json, PolicyStrictSerializerContext.Default.Options);
        return Validate(JsonSerializer.Deserialize(json, PolicyStrictSerializerContext.Default.PolicyDocument));
    }

    public static PolicyDraftDocument? DeserializePolicyDraftDocumentStrict(string json)
    {
        PolicyJsonInput.RejectDuplicatePropertyNames(json, PolicyStrictSerializerContext.Default.Options);
        return Validate(JsonSerializer.Deserialize(json, PolicyStrictSerializerContext.Default.PolicyDraftDocument));
    }

    public static string Serialize<T>(T value)
    {
        ValidateSemanticValue(value);
        return JsonSerializer.Serialize(value, TypeInfo<T>());
    }

    public static T? DeserializeStrict<T>(string json)
    {
        PolicyJsonInput.RejectDuplicatePropertyNames(json, PolicyStrictSerializerContext.Default.Options);
        var value = JsonSerializer.Deserialize(json, StrictTypeInfo<T>());
        ValidateSemanticValue(value);
        return value;
    }

    internal static void ValidateSemanticValue(object? value)
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
            case PackageIdentifierCondition identifiers:
                ValidatePackageIdentifierCondition(identifiers, "$");
                break;
            case VersionCondition version:
                ValidateVersionCondition(version, "$");
                break;
            case VersionRange range:
                ValidateVersionRange(range, "$");
                break;
            case PolicyConstraints constraints:
                ValidateRequiredCollectionElements(constraints, "$");
                break;
        }
    }

    internal static void ValidateRequiredCollectionElements(PolicyDocument policy)
    {
        ValidatePolicyRevision(policy.Metadata.Revision);
        ValidateRequiredCollectionElements(policy.Rules);
    }

    internal static void ValidateRequiredCollectionElements(PolicyDraftDocument policy)
    {
        ValidateRequiredCollectionElements(policy.Rules);
    }

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
        if (IsEmpty(rule.Match))
        {
            throw new JsonException($"The JSON object at {path}.Match must contain at least one effective criterion.");
        }
        if (rule.Decision == Decision.Deny && rule.Constraints is not null)
        {
            throw new JsonException(
                $"The JSON value at {path}.Constraints is valid only when {path}.Decision is Allow.");
        }
        if (rule.Constraints is { } constraints)
        {
            ValidateRequiredCollectionElements(constraints, $"{path}.Constraints");
        }
    }

    private static void ValidateRequiredCollectionElements(PolicyConstraints constraints, string path)
    {
        RejectBoundedStrings(
            constraints.AllowedInstallLocationPatterns,
            1,
            256,
            $"{path}.AllowedInstallLocationPatterns");
        RejectBoundedStrings(
            constraints.AllowedCustomParameters,
            1,
            512,
            $"{path}.AllowedCustomParameters");
        RejectBoundedStrings(
            constraints.AllowedCustomParameterPatterns,
            1,
            512,
            $"{path}.AllowedCustomParameterPatterns");
        RejectBoundedStrings(
            constraints.DeniedCustomParameters,
            1,
            512,
            $"{path}.DeniedCustomParameters");
    }

    private static void ValidateRequiredCollectionElements(PolicyMatch match, string path)
    {
        RejectDuplicateElements(match.Operations, $"{path}.Operations");
        RejectDuplicateElements(match.Managers, $"{path}.Managers");
        RejectDuplicateElements(match.SourceNames, $"{path}.SourceNames");
        RejectDuplicateElements(match.Scopes, $"{path}.Scopes");
        RejectDuplicateElements(match.Architectures, $"{path}.Architectures");
        RejectDuplicateElements(match.ExecutionElevation, $"{path}.ExecutionElevation");
        RejectBoundedStrings(match.SourceNames, 1, 128, $"{path}.SourceNames");
        if (match.SourceNames.Count > 0 && match.Managers.Count != 1)
        {
            throw new JsonException(
                $"The JSON array at {path}.SourceNames requires exactly one value at {path}.Managers.");
        }
        if (match.PackageIdentifiers is { } identifiers)
        {
            ValidatePackageIdentifierCondition(identifiers, $"{path}.PackageIdentifiers");
        }
        if (match.Version is { } version)
        {
            ValidateVersionCondition(version, $"{path}.Version");
        }
    }

    private static void ValidatePackageIdentifierCondition(
        PackageIdentifierCondition identifiers,
        string path)
    {
        if (identifiers.ExactSpecified == identifiers.PatternsSpecified)
        {
            throw new JsonException($"The JSON object at {path} must contain exactly one of Exact or Patterns.");
        }
        if ((identifiers.ExactSpecified && identifiers.Exact is null)
            || (identifiers.PatternsSpecified && identifiers.Patterns is null))
        {
            throw new JsonException($"The selected package identifier mode at {path} must not be null.");
        }
        if (identifiers.Exact is { } exact)
        {
            if (exact.Count is 0 or > 1024)
            {
                throw new JsonException($"The JSON array at {path}.Exact must contain between 1 and 1024 values.");
            }
            RejectPackageIdentifiers(exact, $"{path}.Exact");
            RejectDuplicateElements(exact, $"{path}.Exact");
        }
        if (identifiers.Patterns is { } patterns)
        {
            if (patterns.Count is 0 or > 1024)
            {
                throw new JsonException($"The JSON array at {path}.Patterns must contain between 1 and 1024 values.");
            }
            RejectBoundedStrings(patterns, 1, 256, $"{path}.Patterns");
            RejectDuplicateElements(patterns, $"{path}.Patterns");
        }
    }

    private static void RejectPackageIdentifiers(IReadOnlyList<string> values, string path)
    {
        RejectBoundedStrings(values, 1, 256, path);
        const string AllowedPunctuation = ".-_+@/:[],#$%{}";
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index].Any(character =>
                    !char.IsAsciiLetterOrDigit(character)
                    && !AllowedPunctuation.Contains(character, StringComparison.Ordinal)))
            {
                throw new JsonException(
                    $"The JSON string at {path}[{index}] is not a valid exact package identifier.");
            }
        }
    }

    private static void ValidateVersionCondition(VersionCondition version, string path)
    {
        if (version.ExactSpecified == version.RangeSpecified)
        {
            throw new JsonException($"The JSON object at {path} must contain exactly one of Exact or Range.");
        }
        if ((version.ExactSpecified && version.Exact is null)
            || (version.RangeSpecified && version.Range is null))
        {
            throw new JsonException($"The selected version mode at {path} must not be null.");
        }
        if (version.Exact is { } exact)
        {
            if (exact.Count is 0 or > 256)
            {
                throw new JsonException($"The JSON array at {path}.Exact must contain between 1 and 256 values.");
            }
            RejectBoundedStrings(exact, 1, 128, $"{path}.Exact");
            RejectDuplicateElements(exact, $"{path}.Exact");
        }
        if (version.Range is { } range)
        {
            ValidateVersionRange(range, $"{path}.Range");
        }
    }

    private static void ValidateVersionRange(VersionRange range, string path)
    {
        if (range.MinVersion is null && range.MaxVersion is null)
        {
            throw new JsonException(
                $"The JSON object at {path} must specify MinVersion or MaxVersion.");
        }
        foreach (var (name, value) in new[]
        {
            (nameof(VersionRange.MinVersion), range.MinVersion),
            (nameof(VersionRange.MaxVersion), range.MaxVersion),
        })
        {
            if (value is not null
                && (value.Length > 128 || !SemanticVersionRegex().IsMatch(value)))
            {
                throw new JsonException(
                    $"The JSON string at {path}.{name} must be a canonical semantic version.");
            }
        }
    }

    private static bool IsEmpty(PolicyMatch match) =>
        match.Operations.Count == 0
        && match.Managers.Count == 0
        && match.SourceNames.Count == 0
        && match.PackageIdentifiers is null
        && match.Version is null
        && match.Scopes.Count == 0
        && match.Architectures.Count == 0
        && match.ExecutionElevation.Count == 0
        && match.Interactive is null
        && match.SkipHashCheck is null
        && match.PreRelease is null
        && match.HasCustomParameters is null
        && match.HasCustomInstallLocation is null
        && match.HasPrePostCommands is null
        && match.HasKillBeforeOperation is null
        && match.HasUninstallPrevious is null;

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

    private static void RejectDuplicateElements<T>(IReadOnlyList<T> values, string path)
    {
        var unique = new HashSet<T>();
        for (var index = 0; index < values.Count; index++)
        {
            if (!unique.Add(values[index]))
            {
                throw new JsonException($"The JSON array at {path} must not contain duplicate values.");
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

    private static JsonTypeInfo<T> TypeInfo<T>()
    {
        _ = typeof(T) == typeof(PolicyDocument)
            || typeof(T) == typeof(PolicyDraftDocument)
            || typeof(T) == typeof(PolicyMetadata)
            || typeof(T) == typeof(PolicyDraftMetadata)
            || typeof(T) == typeof(PolicyEnforcement)
            || typeof(T) == typeof(PolicyRule)
            || typeof(T) == typeof(PolicyMatch)
            || typeof(T) == typeof(PackageIdentifierCondition)
            || typeof(T) == typeof(VersionCondition)
            || typeof(T) == typeof(VersionRange)
            || typeof(T) == typeof(PolicyConstraints)
            ? true
            : throw new NotSupportedException(
                $"Policy JSON serialization for {typeof(T).FullName} is not source-generated.");

        return Cast<T>(Options.GetTypeInfo(typeof(T)));
    }

    private static JsonTypeInfo<T> StrictTypeInfo<T>() =>
    typeof(T) == typeof(PolicyDocument) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyDocument) :
    typeof(T) == typeof(PolicyDraftDocument) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyDraftDocument) :
    typeof(T) == typeof(PolicyMetadata) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyMetadata) :
    typeof(T) == typeof(PolicyDraftMetadata) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyDraftMetadata) :
    typeof(T) == typeof(PolicyEnforcement) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyEnforcement) :
    typeof(T) == typeof(PolicyRule) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyRule) :
    typeof(T) == typeof(PolicyMatch) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyMatch) :
    typeof(T) == typeof(PackageIdentifierCondition) ? Cast<T>(PolicyStrictSerializerContext.Default.PackageIdentifierCondition) :
    typeof(T) == typeof(VersionCondition) ? Cast<T>(PolicyStrictSerializerContext.Default.VersionCondition) :
    typeof(T) == typeof(VersionRange) ? Cast<T>(PolicyStrictSerializerContext.Default.VersionRange) :
    typeof(T) == typeof(PolicyConstraints) ? Cast<T>(PolicyStrictSerializerContext.Default.PolicyConstraints) :
    throw new NotSupportedException($"Strict policy JSON deserialization for {typeof(T).FullName} is not source-generated.");

    private static JsonTypeInfo<T> Cast<T>(JsonTypeInfo jsonTypeInfo) =>
        (JsonTypeInfo<T>)jsonTypeInfo;

    private static JsonSerializerOptions CreateOptions(bool strict)
    {
        JsonSerializerContext context = strict
            ? PolicyStrictSerializerContext.Default
            : PolicySerializerContext.Default;
        var options = new JsonSerializerOptions(context.Options)
        {
            TypeInfoResolver = context.WithAddedModifier(AttachSemanticValidation),
        };

        if (strict)
        {
            AddDuplicateRejectingConverters(options, PolicyStrictSerializerContext.Default);
        }
        else
        {
            AddDuplicateRejectingConverters(options, PolicySerializerContext.Default);
        }

        return options;
    }

    private static void AddDuplicateRejectingConverters(
        JsonSerializerOptions options,
        PolicySerializerContext context)
    {
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyDocument>(
            context.PolicyDocument));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyDraftDocument>(
            context.PolicyDraftDocument));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyMetadata>(
            context.PolicyMetadata));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyDraftMetadata>(
            context.PolicyDraftMetadata));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyEnforcement>(
            context.PolicyEnforcement));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyRule>(
            context.PolicyRule));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyMatch>(
            context.PolicyMatch));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PackageIdentifierCondition>(
            context.PackageIdentifierCondition));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<VersionCondition>(
            context.VersionCondition));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<VersionRange>(
            context.VersionRange));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyConstraints>(
            context.PolicyConstraints));
    }

    private static void AddDuplicateRejectingConverters(
        JsonSerializerOptions options,
        PolicyStrictSerializerContext context)
    {
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyDocument>(
            context.PolicyDocument));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyDraftDocument>(
            context.PolicyDraftDocument));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyMetadata>(
            context.PolicyMetadata));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyDraftMetadata>(
            context.PolicyDraftMetadata));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyEnforcement>(
            context.PolicyEnforcement));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyRule>(
            context.PolicyRule));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyMatch>(
            context.PolicyMatch));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PackageIdentifierCondition>(
            context.PackageIdentifierCondition));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<VersionCondition>(
            context.VersionCondition));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<VersionRange>(
            context.VersionRange));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyConstraints>(
            context.PolicyConstraints));
    }

    private static void AttachSemanticValidation(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        ConfigureCanonicalSerialization(typeInfo);
        typeInfo.OnSerializing = ValidateSemanticValue;
        typeInfo.OnDeserialized = ValidateSemanticValue;
    }

    internal static void ConfigureCanonicalSerialization(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(PolicyMatch))
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (IsPolicyMatchCollectionProperty(property.Name))
            {
                property.ShouldSerialize = static (_, value) =>
                    value is System.Collections.ICollection { Count: > 0 };
            }
        }
    }

    private static bool IsPolicyMatchCollectionProperty(string propertyName) =>
        propertyName is
            nameof(PolicyMatch.Operations)
            or nameof(PolicyMatch.Managers)
            or nameof(PolicyMatch.SourceNames)
            or nameof(PolicyMatch.Scopes)
            or nameof(PolicyMatch.Architectures)
            or nameof(PolicyMatch.ExecutionElevation);

    [GeneratedRegex(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionRegex();
}

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
[JsonSerializable(typeof(PackageIdentifierCondition))]
[JsonSerializable(typeof(VersionCondition))]
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
[JsonSerializable(typeof(PackageIdentifierCondition))]
[JsonSerializable(typeof(VersionCondition))]
[JsonSerializable(typeof(VersionRange))]
[JsonSerializable(typeof(PolicyConstraints))]
internal sealed partial class PolicyStrictSerializerContext : JsonSerializerContext;