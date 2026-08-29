using System.Text.Json;
using System.Text.Json.Serialization;

using Devolutions.Now.Policy.Model;

namespace Devolutions.Now.Policy.Api;

internal sealed class ExactCasePolicyManagementStateConverter : ExactCaseStringEnumConverter<PolicyManagementState>;
internal sealed class ExactCasePolicyConfigurationSourceConverter : ExactCaseStringEnumConverter<PolicyConfigurationSource>;
internal sealed class ExactCasePolicyWriteCapabilityConverter : ExactCaseStringEnumConverter<PolicyWriteCapability>;
internal sealed class ExactCasePolicyReadOnlyReasonConverter : ExactCaseStringEnumConverter<PolicyReadOnlyReason>;
internal sealed class ExactCasePolicyReplacementOperationConverter : ExactCaseStringEnumConverter<PolicyReplacementOperation>;
internal sealed class ExactCasePolicyConflictHandlingConverter : ExactCaseStringEnumConverter<PolicyConflictHandling>;
internal sealed class ExactCasePolicyFindingSeverityConverter : ExactCaseStringEnumConverter<PolicyFindingSeverity>;
internal sealed class ExactCasePolicyFindingCodeConverter : ExactCaseStringEnumConverter<PolicyFindingCode>;

internal abstract class OpaqueStringJsonConverter(int maxLength, string typeName) : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"{typeName} must be a string.");
        }

        return Validate(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(Validate(value));

    private string Validate(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength)
        {
            throw new JsonException($"{typeName} must contain between 1 and {maxLength} characters.");
        }
        if (!IsAsciiAlphanumeric(value[0]) || value.Any(character =>
                !IsAsciiAlphanumeric(character) && character is not ('.' or '_' or '~' or ':' or '-')))
        {
            throw new JsonException(
                $"{typeName} must use safe printable ASCII characters and start with an ASCII alphanumeric character.");
        }

        return value;
    }

    private static bool IsAsciiAlphanumeric(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}

internal sealed class PolicyStoreTokenJsonConverter()
    : OpaqueStringJsonConverter(512, "PolicyStoreToken");

internal sealed class PolicyValidationReceiptJsonConverter()
    : OpaqueStringJsonConverter(2048, "PolicyValidationReceipt");

/// <summary>Current configured-policy state.</summary>
[JsonConverter(typeof(ExactCasePolicyManagementStateConverter))]
public enum PolicyManagementState
{
    Active,
    Missing,
    Invalid,
}

/// <summary>Origin of the resolved policy path.</summary>
[JsonConverter(typeof(ExactCasePolicyConfigurationSourceConverter))]
public enum PolicyConfigurationSource
{
    DefaultPath,
    ConfiguredPath,
}

/// <summary>Advisory ability to write the configured policy through the management API.</summary>
[JsonConverter(typeof(ExactCasePolicyWriteCapabilityConverter))]
public enum PolicyWriteCapability
{
    Writable,
    ReadOnly,
    Unsupported,
}

/// <summary>Stable reason why the configured policy cannot be written.</summary>
[JsonConverter(typeof(ExactCasePolicyReadOnlyReasonConverter))]
public enum PolicyReadOnlyReason
{
    ManagementDisabled,
    PathNotConfigured,
    UnsafePath,
    InsufficientPermissions,
    UnsupportedFileSystem,
}

/// <summary>Requested identity/revision behavior for a policy replacement.</summary>
[JsonConverter(typeof(ExactCasePolicyReplacementOperationConverter))]
public enum PolicyReplacementOperation
{
    Update,
    ReplaceIdentity,
    Create,
    Repair,
}

/// <summary>Optimistic-conflict behavior for policy replacement.</summary>
[JsonConverter(typeof(ExactCasePolicyConflictHandlingConverter))]
public enum PolicyConflictHandling
{
    Reject,
    ConfirmOverwrite,
}

/// <summary>Severity of a policy validation finding.</summary>
[JsonConverter(typeof(ExactCasePolicyFindingSeverityConverter))]
public enum PolicyFindingSeverity
{
    Error,
    Warning,
}

/// <summary>Stable policy validation finding code.</summary>
[JsonConverter(typeof(ExactCasePolicyFindingCodeConverter))]
public enum PolicyFindingCode
{
    SchemaViolation,
    UnknownField,
    MissingRequiredField,
    InvalidFieldType,
    InvalidFieldValue,
    DuplicateRuleId,
    IneffectiveBooleanMatch,
    InvalidVersionRange,
    EmptyVersionRange,
    InvalidWildcardPattern,
    ContradictoryConstraints,
    InvalidValidityInterval,
    UnsupportedSchema,
    UnsupportedPolicyType,
    UnsupportedPolicyVersion,
    AuditModeEnabled,
    DefaultAllow,
    SensitiveOptionAllowed,
}

/// <summary>Versioned, structured policy validation finding.</summary>
public sealed class PolicyFinding
{
    [JsonPropertyName("FindingVersion")]
    [JsonRequired]
    public string FindingVersion { get; set; } = BrokerApi.Version;

    [JsonPropertyName("Severity")]
    [JsonRequired]
    public PolicyFindingSeverity Severity { get; set; }

    [JsonPropertyName("Code")]
    [JsonRequired]
    public PolicyFindingCode Code { get; set; }

    /// <summary>RFC 6901 JSON Pointer into the submitted draft.</summary>
    [JsonPropertyName("Path")]
    [JsonRequired]
    public string Path { get; set; } = "";

    [JsonPropertyName("RuleId")]
    public string? RuleId { get; set; }

    /// <summary>Machine-readable message arguments for localization.</summary>
    [JsonPropertyName("Arguments")]
    public Dictionary<string, JsonElement> Arguments { get; set; } = [];

    /// <summary>Human-readable fallback for clients that do not recognize the code.</summary>
    [JsonPropertyName("Message")]
    [JsonRequired]
    public string Message { get; set; } = "";
}

/// <summary>Authoritative policy validation output.</summary>
public sealed class PolicyValidationResult
{
    [JsonPropertyName("ResultVersion")]
    [JsonRequired]
    public string ResultVersion { get; set; } = BrokerApi.Version;

    [JsonPropertyName("ValidatorVersion")]
    [JsonRequired]
    public string ValidatorVersion { get; set; } = "";

    [JsonPropertyName("IsValid")]
    [JsonRequired]
    public bool IsValid { get; set; }

    [JsonPropertyName("CanonicalDraft")]
    public PolicyDraftDocument? CanonicalDraft { get; set; }

    [JsonPropertyName("ValidationReceipt")]
    [JsonConverter(typeof(PolicyValidationReceiptJsonConverter))]
    public string? ValidationReceipt { get; set; }

    [JsonPropertyName("Findings")]
    [JsonRequired]
    public List<PolicyFinding> Findings { get; set; } = [];
}

/// <summary>Sanitized diagnostics for an invalid configured policy.</summary>
public sealed class InvalidPolicyDiagnostics
{
    [JsonPropertyName("DiagnosticsVersion")]
    [JsonRequired]
    public string DiagnosticsVersion { get; set; } = BrokerApi.Version;

    [JsonPropertyName("Findings")]
    [JsonRequired]
    public List<PolicyFinding> Findings { get; set; } = [];
}

/// <summary>Atomic view of configured policy state and management guidance.</summary>
public sealed class PolicyManagementSnapshot
{
    [JsonPropertyName("State")]
    [JsonRequired]
    public PolicyManagementState State { get; set; }

    [JsonPropertyName("ConfiguredPath")]
    [JsonRequired]
    public string ConfiguredPath { get; set; } = "";

    [JsonPropertyName("StoreToken")]
    [JsonRequired]
    [JsonConverter(typeof(PolicyStoreTokenJsonConverter))]
    public string StoreToken { get; set; } = "";

    [JsonPropertyName("Source")]
    [JsonRequired]
    public PolicyConfigurationSource Source { get; set; }

    [JsonPropertyName("WriteCapability")]
    [JsonRequired]
    public PolicyWriteCapability WriteCapability { get; set; }

    [JsonPropertyName("ReadOnlyReason")]
    public PolicyReadOnlyReason? ReadOnlyReason { get; set; }

    [JsonPropertyName("ElevationRequired")]
    [JsonRequired]
    public bool ElevationRequired { get; set; }

    [JsonPropertyName("Policy")]
    public PolicyDocument? Policy { get; set; }

    [JsonPropertyName("InvalidDiagnostics")]
    public InvalidPolicyDiagnostics? InvalidDiagnostics { get; set; }
}

/// <summary>Response body for <c>GET /v1/policy/management</c>.</summary>
public sealed class PolicyManagementResponse
{
    private const string Kind = BrokerApi.PolicyManagementResponseKind;
    private string _responseKind = Kind;

    [JsonPropertyName("ResponseKind")]
    [JsonRequired]
    public string ResponseKind
    {
        get => _responseKind;
        set => _responseKind = BrokerApi.ValidateMessageKind(value, Kind, nameof(ResponseKind));
    }

    [JsonPropertyName("ResponseVersion")]
    [JsonRequired]
    public string ResponseVersion { get; set; } = BrokerApi.Version;

    [JsonPropertyName("Server")]
    [JsonRequired]
    public ServerContext Server { get; set; } = new();

    [JsonPropertyName("Management")]
    [JsonRequired]
    public PolicyManagementSnapshot Management { get; set; } = new();
}

/// <summary>Request body for <c>POST /v1/policy/validate</c>.</summary>
public sealed class PolicyValidationRequest
{
    private const string Kind = BrokerApi.PolicyValidationRequestKind;
    private string _requestKind = Kind;

    [JsonPropertyName("RequestKind")]
    [JsonRequired]
    public string RequestKind
    {
        get => _requestKind;
        set => _requestKind = BrokerApi.ValidateMessageKind(value, Kind, nameof(RequestKind));
    }

    [JsonPropertyName("RequestVersion")]
    [JsonRequired]
    public string RequestVersion { get; set; } = BrokerApi.Version;

    /// <summary>Raw draft JSON retained without dropping unknown members.</summary>
    [JsonPropertyName("Draft")]
    [JsonRequired]
    public JsonElement Draft { get; set; }
}

/// <summary>Response body for <c>POST /v1/policy/validate</c>.</summary>
public sealed class PolicyValidationResponse
{
    private const string Kind = BrokerApi.PolicyValidationResponseKind;
    private string _responseKind = Kind;

    [JsonPropertyName("ResponseKind")]
    [JsonRequired]
    public string ResponseKind
    {
        get => _responseKind;
        set => _responseKind = BrokerApi.ValidateMessageKind(value, Kind, nameof(ResponseKind));
    }

    [JsonPropertyName("ResponseVersion")]
    [JsonRequired]
    public string ResponseVersion { get; set; } = BrokerApi.Version;

    [JsonPropertyName("Server")]
    [JsonRequired]
    public ServerContext Server { get; set; } = new();

    [JsonPropertyName("Validation")]
    [JsonRequired]
    public PolicyValidationResult Validation { get; set; } = new();
}

/// <summary>Request body for <c>PUT /v1/policy</c>.</summary>
public sealed class PolicyReplacementRequest
{
    private const string Kind = BrokerApi.PolicyReplacementRequestKind;
    private string _requestKind = Kind;

    [JsonPropertyName("RequestKind")]
    [JsonRequired]
    public string RequestKind
    {
        get => _requestKind;
        set => _requestKind = BrokerApi.ValidateMessageKind(value, Kind, nameof(RequestKind));
    }

    [JsonPropertyName("RequestVersion")]
    [JsonRequired]
    public string RequestVersion { get; set; } = BrokerApi.Version;

    [JsonPropertyName("ExpectedStoreToken")]
    [JsonRequired]
    [JsonConverter(typeof(PolicyStoreTokenJsonConverter))]
    public string ExpectedStoreToken { get; set; } = "";

    [JsonPropertyName("Operation")]
    [JsonRequired]
    public PolicyReplacementOperation Operation { get; set; }

    [JsonPropertyName("ConflictHandling")]
    [JsonRequired]
    public PolicyConflictHandling ConflictHandling { get; set; }

    [JsonPropertyName("WarningsAcknowledged")]
    [JsonRequired]
    public bool WarningsAcknowledged { get; set; }

    /// <summary>Raw draft JSON retained for transaction-time reparsing and revalidation.</summary>
    [JsonPropertyName("Draft")]
    [JsonRequired]
    public JsonElement Draft { get; set; }

    [JsonPropertyName("ValidationReceipt")]
    [JsonRequired]
    [JsonConverter(typeof(PolicyValidationReceiptJsonConverter))]
    public string ValidationReceipt { get; set; } = "";
}

/// <summary>Response body for <c>PUT /v1/policy</c>.</summary>
public sealed class PolicyReplacementResponse
{
    private const string Kind = BrokerApi.PolicyReplacementResponseKind;
    private string _responseKind = Kind;

    [JsonPropertyName("ResponseKind")]
    [JsonRequired]
    public string ResponseKind
    {
        get => _responseKind;
        set => _responseKind = BrokerApi.ValidateMessageKind(value, Kind, nameof(ResponseKind));
    }

    [JsonPropertyName("ResponseVersion")]
    [JsonRequired]
    public string ResponseVersion { get; set; } = BrokerApi.Version;

    [JsonPropertyName("Server")]
    [JsonRequired]
    public ServerContext Server { get; set; } = new();

    /// <summary>Exact committed active policy, including server-assigned metadata.</summary>
    [JsonPropertyName("Policy")]
    [JsonRequired]
    public PolicyDocument Policy { get; set; } = new();

    [JsonPropertyName("Validation")]
    [JsonRequired]
    public PolicyValidationResult Validation { get; set; } = new();

    /// <summary>Newly observed management state and store token.</summary>
    [JsonPropertyName("Management")]
    [JsonRequired]
    public PolicyManagementSnapshot Management { get; set; } = new();
}