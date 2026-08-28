using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Api;

// Enum members are spelled exactly as they appear on the wire (PascalCase), so the
// default JsonStringEnumConverter round-trips them without a naming policy.

internal class ExactCaseStringEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a string value for {typeof(TEnum).Name}.");
        }

        var name = reader.GetString();
        if (name is null ||
            !Enum.TryParse<TEnum>(name, ignoreCase: false, out var value) ||
            Enum.GetName(value) != name)
        {
            throw new JsonException($"'{name}' is not a canonical {typeof(TEnum).Name} value.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        var name = Enum.GetName(value)
            ?? throw new JsonException($"'{value}' is not a defined {typeof(TEnum).Name} value.");
        writer.WriteStringValue(name);
    }
}

internal sealed class ExactCaseTransportConverter : ExactCaseStringEnumConverter<Transport>;
internal sealed class ExactCaseErrorCodeConverter : ExactCaseStringEnumConverter<ErrorCode>;

/// <summary>Package operation type.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Operation>))]
public enum Operation
{
    Install,
    Update,
    Uninstall,
}

/// <summary>Supported package manager names.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ManagerName>))]
public enum ManagerName
{
    Winget,
    PowerShell,
    PowerShell7,
    Apt,
    Bun,
    Cargo,
    Chocolatey,
    Dnf,
    Dotnet,
    Flatpak,
    Homebrew,
    Npm,
    Pacman,
    Pip,
    Scoop,
    Snap,
    Vcpkg,
}

/// <summary>Installation scope.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Scope>))]
public enum Scope
{
    User,
    Machine,
}

/// <summary>Target architecture.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Architecture>))]
public enum Architecture
{
    X86,
    X64,
    Arm64,
    Neutral,
}

/// <summary>Requested elevation level.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Elevation>))]
public enum Elevation
{
    Standard,
    Elevated,
}

/// <summary>Policy decision.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Decision>))]
public enum Decision
{
    Allow,
    Deny,
}

/// <summary>Broker transport type.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Transport>))]
public enum Transport
{
    HttpNamedPipe,
    HttpLoopbackSimulator,
}

/// <summary>Status of an asynchronous package operation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OperationStatus>))]
public enum OperationStatus
{
    Starting,
    Running,
    Completed,
    Failed,
    Canceling,
    Canceled,
}

/// <summary>Broker readiness state reported by the health endpoint.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthStatus>))]
public enum HealthStatus
{
    Ready,
    Paused,
}

/// <summary>Structured machine-readable error code.</summary>
[JsonConverter(typeof(ExactCaseErrorCodeConverter))]
public enum ErrorCode
{
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    PayloadTooLarge,
    UnsupportedMediaType,
    ValidationFailed,
    BrokerPaused,
    InternalError,
    Timeout,
    UnsupportedEndpoint,
    MalformedDraft,
    InvalidPolicy,
    WarningConfirmationRequired,
    Unauthenticated,
    AdministratorRequired,
    UnsafePolicyPath,
    StalePolicyStoreToken,
    UnsupportedPolicyFilesystem,
    PolicyPersistenceFailed,
    PolicyActivationFailed,
}

/// <summary>Transport kind of a per-operation event channel.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EventChannelKind>))]
public enum EventChannelKind
{
    /// <summary>Local named pipe carrying <c>NOW_BROKER</c> event frames.</summary>
    LocalPipe,
}