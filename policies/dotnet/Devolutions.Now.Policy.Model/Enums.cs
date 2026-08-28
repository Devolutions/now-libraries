using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Model;

internal sealed class ExactCaseStringEnumConverter<TEnum> : JsonConverter<TEnum>
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

/// <summary>Package operation type.</summary>
[JsonConverter(typeof(ExactCaseStringEnumConverter<Operation>))]
public enum Operation
{
    Install,
    Update,
    Uninstall,
}

/// <summary>Supported package manager names.</summary>
[JsonConverter(typeof(ExactCaseStringEnumConverter<ManagerName>))]
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
[JsonConverter(typeof(ExactCaseStringEnumConverter<Scope>))]
public enum Scope
{
    User,
    Machine,
}

/// <summary>Target architecture.</summary>
[JsonConverter(typeof(ExactCaseStringEnumConverter<Architecture>))]
public enum Architecture
{
    X86,
    X64,
    Arm64,
    Neutral,
}

/// <summary>Requested elevation level.</summary>
[JsonConverter(typeof(ExactCaseStringEnumConverter<Elevation>))]
public enum Elevation
{
    Standard,
    Elevated,
}

/// <summary>Policy decision.</summary>
[JsonConverter(typeof(ExactCaseStringEnumConverter<Decision>))]
public enum Decision
{
    Allow,
    Deny,
}

/// <summary>Rule precedence strategy.</summary>
[JsonConverter(typeof(ExactCaseStringEnumConverter<RulePrecedence>))]
public enum RulePrecedence
{
    PriorityThenDeny,
}