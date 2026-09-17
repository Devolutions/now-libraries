using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Devolutions.Now.Policy.Model;

namespace Devolutions.Now.Policy.Api;

/// <summary>Shared <see cref="JsonSerializerOptions"/> for broker documents.</summary>
public static class BrokerSerializer
{
    /// <summary>
    /// Serialization options matching the broker wire format: PascalCase property names
    /// (via explicit <c>[JsonPropertyName]</c> attributes), PascalCase enum values, and
    /// null optionals omitted (mirroring the Rust <c>skip_serializing_if = "Option::is_none"</c>).
    /// Deserialization rejects duplicate property names throughout the input using ordinal,
    /// case-sensitive name comparison.
    /// </summary>
    public static readonly JsonSerializerOptions Options = CreateOptions(writeIndented: false);

    public static readonly JsonSerializerOptions PrettyOptions = CreateOptions(writeIndented: true);

    public static string Serialize<T>(T value)
    {
        ValidateSemanticValue(value);
        return JsonSerializer.Serialize(value, TypeInfo<T>());
    }

    public static T? Deserialize<T>(string json)
    {
        PolicyJsonInput.RejectDuplicatePropertyNames(json, BrokerSerializerContext.Default.Options);
        var value = JsonSerializer.Deserialize(json, TypeInfo<T>());
        ValidateSemanticValue(value);
        return value;
    }

    public static T? DeserializeStrict<T>(string json)
    {
        PolicyJsonInput.RejectDuplicatePropertyNames(json, BrokerStrictSerializerContext.Default.Options);
        var value = JsonSerializer.Deserialize(json, StrictTypeInfo<T>());
        ValidateSemanticValue(value);
        return value;
    }

    private static void ValidateSemanticValue(object? value)
    {
        switch (value)
        {
            case PolicyDocument policy:
                PolicySerializer.ValidateRequiredCollectionElements(policy);
                break;
            case PolicyDraftDocument draft:
                PolicySerializer.ValidateRequiredCollectionElements(draft);
                break;
            case PolicyMetadata
                or PolicyRule
                or PolicyMatch
                or PackageIdentifierCondition
                or VersionCondition
                or VersionRange
                or PolicyConstraints:
                PolicySerializer.ValidateSemanticValue(value);
                break;
            case PolicyResponse response:
                PolicySerializer.ValidateRequiredCollectionElements(response.Policy);
                break;
            case PolicyManagementResponse response:
                ValidateManagement(response.Management);
                break;
            case PolicyValidationResponse response:
                ValidateValidation(response.Validation);
                break;
            case PolicyReplacementResponse response:
                ValidateReplacement(response);
                break;
            case PolicyValidationResult validation:
                ValidateValidation(validation);
                break;
            case PolicyManagementSnapshot management:
                ValidateManagement(management);
                break;
            case ErrorResponse error:
                ValidateError(error);
                break;
        }
    }

    private static JsonTypeInfo<T> TypeInfo<T>()
    {
        _ = typeof(T) == typeof(PackageRequest)
            || typeof(T) == typeof(StatusRequest)
            || typeof(T) == typeof(CancelRequest)
            || typeof(T) == typeof(PolicyValidationRequest)
            || typeof(T) == typeof(PolicyReplacementRequest)
            || typeof(T) == typeof(HealthResponse)
            || typeof(T) == typeof(CapabilitiesResponse)
            || typeof(T) == typeof(PolicyResponse)
            || typeof(T) == typeof(PolicyManagementResponse)
            || typeof(T) == typeof(PolicyValidationResponse)
            || typeof(T) == typeof(PolicyReplacementResponse)
            || typeof(T) == typeof(EvaluationResponse)
            || typeof(T) == typeof(ExecutionResponse)
            || typeof(T) == typeof(StatusResponse)
            || typeof(T) == typeof(CancelResponse)
            || typeof(T) == typeof(ErrorResponse)
            ? true
            : throw new NotSupportedException(
                $"Broker JSON serialization for {typeof(T).FullName} is not source-generated.");

        return Cast<T>(Options.GetTypeInfo(typeof(T)));
    }

    private static JsonTypeInfo<T> StrictTypeInfo<T>() =>
        typeof(T) == typeof(PackageRequest) ? Cast<T>(BrokerStrictSerializerContext.Default.PackageRequest) :
        typeof(T) == typeof(StatusRequest) ? Cast<T>(BrokerStrictSerializerContext.Default.StatusRequest) :
        typeof(T) == typeof(CancelRequest) ? Cast<T>(BrokerStrictSerializerContext.Default.CancelRequest) :
        typeof(T) == typeof(PolicyValidationRequest) ? Cast<T>(BrokerPolicyStrictSerializerContext.Default.PolicyValidationRequest) :
        typeof(T) == typeof(PolicyReplacementRequest) ? Cast<T>(BrokerPolicyStrictSerializerContext.Default.PolicyReplacementRequest) :
        typeof(T) == typeof(HealthResponse) ? Cast<T>(BrokerStrictSerializerContext.Default.HealthResponse) :
        typeof(T) == typeof(CapabilitiesResponse) ? Cast<T>(BrokerStrictSerializerContext.Default.CapabilitiesResponse) :
        typeof(T) == typeof(PolicyResponse) ? Cast<T>(BrokerPolicyStrictSerializerContext.Default.PolicyResponse) :
        typeof(T) == typeof(PolicyManagementResponse) ? Cast<T>(BrokerPolicyStrictSerializerContext.Default.PolicyManagementResponse) :
        typeof(T) == typeof(PolicyValidationResponse) ? Cast<T>(BrokerPolicyStrictSerializerContext.Default.PolicyValidationResponse) :
        typeof(T) == typeof(PolicyReplacementResponse) ? Cast<T>(BrokerPolicyStrictSerializerContext.Default.PolicyReplacementResponse) :
        typeof(T) == typeof(EvaluationResponse) ? Cast<T>(BrokerStrictSerializerContext.Default.EvaluationResponse) :
        typeof(T) == typeof(ExecutionResponse) ? Cast<T>(BrokerStrictSerializerContext.Default.ExecutionResponse) :
        typeof(T) == typeof(StatusResponse) ? Cast<T>(BrokerStrictSerializerContext.Default.StatusResponse) :
        typeof(T) == typeof(CancelResponse) ? Cast<T>(BrokerStrictSerializerContext.Default.CancelResponse) :
        typeof(T) == typeof(ErrorResponse) ? Cast<T>(BrokerErrorStrictSerializerContext.Default.ErrorResponse) :
        throw new NotSupportedException($"Strict broker JSON deserialization for {typeof(T).FullName} is not source-generated.");

    private static JsonTypeInfo<T> Cast<T>(JsonTypeInfo jsonTypeInfo) =>
        (JsonTypeInfo<T>)jsonTypeInfo;

    private static void ValidateManagement(PolicyManagementSnapshot management)
    {
        switch (management.State)
        {
            case PolicyManagementState.Active when management.Policy is null || management.InvalidDiagnostics is not null:
                throw new JsonException("Active management snapshots require Policy and forbid InvalidDiagnostics.");
            case PolicyManagementState.Missing when management.Policy is not null || management.InvalidDiagnostics is not null:
                throw new JsonException("Missing management snapshots forbid Policy and InvalidDiagnostics.");
            case PolicyManagementState.Invalid:
                if (management.Policy is not null)
                {
                    throw new JsonException("Invalid management snapshots forbid Policy.");
                }
                RejectNullElements(management.InvalidDiagnostics?.Findings, "Management.InvalidDiagnostics.Findings");
                if (management.InvalidDiagnostics is null
                    || management.InvalidDiagnostics.Findings.Count == 0
                    || !management.InvalidDiagnostics.Findings.Any(
                        finding => finding.Severity == PolicyFindingSeverity.Error))
                {
                    throw new JsonException(
                        "Invalid management snapshots require nonempty diagnostics with an Error finding.");
                }
                break;
        }

        switch (management.WriteCapability)
        {
            case PolicyWriteCapability.Writable when management.ReadOnlyReason is not null:
                throw new JsonException("Writable management snapshots forbid ReadOnlyReason.");
            case PolicyWriteCapability.ReadOnly or PolicyWriteCapability.Unsupported
                when management.ReadOnlyReason is null:
                throw new JsonException("ReadOnly and Unsupported management snapshots require ReadOnlyReason.");
        }

        if (management.Policy is { } policy)
        {
            PolicySerializer.ValidateRequiredCollectionElements(policy);
        }
    }

    private static void ValidateError(ErrorResponse error)
    {
        if (error.Code == ErrorCode.StalePolicyStoreToken && error.Management is null)
        {
            throw new JsonException("StalePolicyStoreToken errors require Management.");
        }

        if (error.Management is { } management)
        {
            ValidateManagement(management);
        }

        if (error.Validation is { } validation)
        {
            ValidateValidation(validation);
        }
    }

    private static void ValidateValidation(PolicyValidationResult validation)
    {
        RejectNullElements(validation.Findings, "Validation.Findings");
        var hasError = validation.Findings.Any(finding => finding.Severity == PolicyFindingSeverity.Error);
        if (validation.IsValid)
        {
            if (validation.CanonicalDraft is null || validation.ValidationReceipt is null)
            {
                throw new JsonException(
                    "Valid policy validation results require CanonicalDraft and ValidationReceipt.");
            }
            if (hasError)
            {
                throw new JsonException("Valid policy validation results must not contain Error findings.");
            }

            PolicySerializer.ValidateRequiredCollectionElements(validation.CanonicalDraft);
        }
        else
        {
            if (validation.CanonicalDraft is not null || validation.ValidationReceipt is not null)
            {
                throw new JsonException(
                    "Invalid policy validation results must not contain CanonicalDraft or ValidationReceipt.");
            }
            if (!hasError)
            {
                throw new JsonException(
                    "Invalid policy validation results require at least one Error finding.");
            }
        }
    }

    private static void ValidateReplacement(PolicyReplacementResponse response)
    {
        PolicySerializer.ValidateRequiredCollectionElements(response.Policy);
        ValidateValidation(response.Validation);
        ValidateManagement(response.Management);

        if (!response.Validation.IsValid)
        {
            throw new JsonException("Policy replacement responses require a valid Validation result.");
        }
        if (response.Management.State != PolicyManagementState.Active)
        {
            throw new JsonException("Policy replacement responses require an Active Management snapshot.");
        }
        if (response.Management.Policy is null
            || !PolicyDocumentsEqual(response.Policy, response.Management.Policy))
        {
            throw new JsonException(
                "Policy replacement responses require Policy and Management.Policy to match.");
        }
        if (response.Validation.CanonicalDraft is null
            || !PolicyDraftDocumentsEqual(response.Policy.ToDraft(), response.Validation.CanonicalDraft))
        {
            throw new JsonException(
                "Policy replacement responses require Policy and Validation.CanonicalDraft to match.");
        }
    }

    private static bool PolicyDocumentsEqual(PolicyDocument left, PolicyDocument right) =>
        string.Equals(PolicySerializer.Serialize(left), PolicySerializer.Serialize(right), StringComparison.Ordinal);

    private static bool PolicyDraftDocumentsEqual(PolicyDraftDocument left, PolicyDraftDocument right) =>
        string.Equals(PolicySerializer.Serialize(left), PolicySerializer.Serialize(right), StringComparison.Ordinal);

    private static void RejectNullElements<T>(IReadOnlyList<T>? values, string path)
        where T : class
    {
        if (values is null)
        {
            throw new JsonException($"The JSON array at {path} must not be null.");
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is null)
            {
                throw new JsonException($"The JSON value at {path}[{index}] must not be null.");
            }
        }
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var resolver = JsonTypeInfoResolver.Combine(
                BrokerSerializerContext.Default,
                BrokerPolicySerializerContext.Default,
                BrokerErrorSerializerContext.Default)
            .WithAddedModifier(AttachSemanticValidation);

        var options = new JsonSerializerOptions(BrokerSerializerContext.Default.Options)
        {
            TypeInfoResolver = resolver,
            WriteIndented = writeIndented,
        };

        AddDuplicateRejectingConverters(options);
        return options;
    }

    private static void AddDuplicateRejectingConverters(JsonSerializerOptions options)
    {
        // Explicit closed-world registrations are Native AOT safe. The client test suite enumerates
        // all public object metadata from these source-generated contexts to prevent omissions.
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PackageRequest>(
            BrokerSerializerContext.Default.PackageRequest));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<RequestSource>(
            BrokerSerializerContext.Default.RequestSource));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<RequestPackage>(
            BrokerSerializerContext.Default.RequestPackage));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<RequestOptions>(
            BrokerSerializerContext.Default.RequestOptions));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<ClientContext>(
            BrokerSerializerContext.Default.ClientContext));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<StatusRequest>(
            BrokerSerializerContext.Default.StatusRequest));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<CancelRequest>(
            BrokerSerializerContext.Default.CancelRequest));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyValidationRequest>(
            BrokerPolicySerializerContext.Default.PolicyValidationRequest));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyReplacementRequest>(
            BrokerPolicySerializerContext.Default.PolicyReplacementRequest));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<HealthResponse>(
            BrokerSerializerContext.Default.HealthResponse));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<CapabilitiesResponse>(
            BrokerSerializerContext.Default.CapabilitiesResponse));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<ManagerCapability>(
            BrokerSerializerContext.Default.ManagerCapability));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyResponse>(
            BrokerPolicySerializerContext.Default.PolicyResponse));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyManagementResponse>(
            BrokerPolicySerializerContext.Default.PolicyManagementResponse));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyValidationResponse>(
            BrokerPolicySerializerContext.Default.PolicyValidationResponse));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyReplacementResponse>(
            BrokerPolicySerializerContext.Default.PolicyReplacementResponse));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<EvaluationResponse>(
            BrokerSerializerContext.Default.EvaluationResponse));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<ExecutionResponse>(
            BrokerSerializerContext.Default.ExecutionResponse));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<ServerContext>(
            BrokerSerializerContext.Default.ServerContext));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<RequestSummary>(
            BrokerSerializerContext.Default.RequestSummary));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<DecisionInfo>(
            BrokerSerializerContext.Default.DecisionInfo));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<ResponsePolicyInfo>(
            BrokerSerializerContext.Default.ResponsePolicyInfo));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<OperationDiagnostics>(
            BrokerSerializerContext.Default.OperationDiagnostics));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<OperationSubmission>(
            BrokerSerializerContext.Default.OperationSubmission));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<EventChannel>(
            BrokerSerializerContext.Default.EventChannel));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<StatusResponse>(
            BrokerSerializerContext.Default.StatusResponse));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<CancelResponse>(
            BrokerSerializerContext.Default.CancelResponse));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<ErrorResponse>(
            BrokerErrorSerializerContext.Default.ErrorResponse));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<ErrorDetail>(
            BrokerErrorSerializerContext.Default.ErrorDetail));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyDocument>(
            BrokerPolicySerializerContext.Default.PolicyDocument));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyDraftDocument>(
            BrokerPolicySerializerContext.Default.PolicyDraftDocument));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyMetadata>(
            BrokerPolicySerializerContext.Default.PolicyMetadata));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyDraftMetadata>(
            BrokerPolicySerializerContext.Default.PolicyDraftMetadata));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyEnforcement>(
            BrokerPolicySerializerContext.Default.PolicyEnforcement));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyRule>(
            BrokerPolicySerializerContext.Default.PolicyRule));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyMatch>(
            BrokerPolicySerializerContext.Default.PolicyMatch));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PackageIdentifierCondition>(
            BrokerPolicySerializerContext.Default.PackageIdentifierCondition));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<VersionCondition>(
            BrokerPolicySerializerContext.Default.VersionCondition));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<VersionRange>(
            BrokerPolicySerializerContext.Default.VersionRange));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyConstraints>(
            BrokerPolicySerializerContext.Default.PolicyConstraints));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyFinding>(
            BrokerPolicySerializerContext.Default.PolicyFinding));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyValidationResult>(
            BrokerPolicySerializerContext.Default.PolicyValidationResult));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<InvalidPolicyDiagnostics>(
            BrokerPolicySerializerContext.Default.InvalidPolicyDiagnostics));
        options.Converters.Add(new DuplicatePropertyNameRejectingConverter<PolicyManagementSnapshot>(
            BrokerPolicySerializerContext.Default.PolicyManagementSnapshot));
    }

    private static void AttachSemanticValidation(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        if (typeInfo.Type.Assembly == typeof(PolicyDocument).Assembly)
        {
            typeInfo.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        }
        PolicySerializer.ConfigureCanonicalSerialization(typeInfo);
        typeInfo.OnSerializing = ValidateSemanticValue;
        typeInfo.OnDeserialized = ValidateSemanticValue;
    }
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
internal sealed partial class BrokerSerializerContext : JsonSerializerContext;

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
internal sealed partial class BrokerStrictSerializerContext : JsonSerializerContext;

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
internal sealed partial class BrokerPolicySerializerContext : JsonSerializerContext;

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
internal sealed partial class BrokerPolicyStrictSerializerContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    WriteIndented = false)]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class BrokerErrorSerializerContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    WriteIndented = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class BrokerErrorStrictSerializerContext : JsonSerializerContext;