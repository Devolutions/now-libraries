using System.Text.Json;

namespace Devolutions.Now.Policy.Api;

/// <summary>Constants shared by package broker API clients and implementations.</summary>
public static class BrokerApi
{
    public const string Version = "1.0";

    public const string DefaultPipeName = "Devolutions.Now.PackageBroker.v1";

    public const string PackageRequestKind = "PackageRequest";
    public const string StatusRequestKind = "StatusRequest";
    public const string CancelRequestKind = "CancelRequest";
    public const string PolicyValidationRequestKind = "PolicyValidationRequest";
    public const string PolicyReplacementRequestKind = "PolicyReplacementRequest";

    public const string HealthResponseKind = "HealthResponse";
    public const string CapabilitiesResponseKind = "CapabilitiesResponse";
    public const string EvaluationResponseKind = "EvaluationResponse";
    public const string ExecutionResponseKind = "ExecutionResponse";
    public const string StatusResponseKind = "StatusResponse";
    public const string CancelResponseKind = "CancelResponse";
    public const string PolicyResponseKind = "PolicyResponse";
    public const string PolicyManagementResponseKind = "PolicyManagementResponse";
    public const string PolicyValidationResponseKind = "PolicyValidationResponse";
    public const string PolicyReplacementResponseKind = "PolicyReplacementResponse";
    public const string ErrorResponseKind = "ErrorResponse";

    internal static string ValidateMessageKind(string? value, string expected, string propertyName)
    {
        if (value == expected)
        {
            return value;
        }

        throw new JsonException($"{propertyName} must be '{expected}', but was '{value ?? "<null>"}'.");
    }
}