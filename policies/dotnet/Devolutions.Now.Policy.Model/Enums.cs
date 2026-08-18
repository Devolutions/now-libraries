using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Model;

internal sealed class StringOnlyEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public StringOnlyEnumConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}

/// <summary>Package operation type.</summary>
[JsonConverter(typeof(StringOnlyEnumConverter<Operation>))]
public enum Operation
{
    Install,
    Update,
    Uninstall,
}

/// <summary>Supported package manager names.</summary>
[JsonConverter(typeof(StringOnlyEnumConverter<ManagerName>))]
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
[JsonConverter(typeof(StringOnlyEnumConverter<Scope>))]
public enum Scope
{
    User,
    Machine,
}

/// <summary>Target architecture.</summary>
[JsonConverter(typeof(StringOnlyEnumConverter<Architecture>))]
public enum Architecture
{
    X86,
    X64,
    Arm64,
    Neutral,
}

/// <summary>Requested elevation level.</summary>
[JsonConverter(typeof(StringOnlyEnumConverter<Elevation>))]
public enum Elevation
{
    Standard,
    Elevated,
}

/// <summary>Policy decision.</summary>
[JsonConverter(typeof(StringOnlyEnumConverter<Decision>))]
public enum Decision
{
    Allow,
    Deny,
}

/// <summary>Rule precedence strategy.</summary>
[JsonConverter(typeof(StringOnlyEnumConverter<RulePrecedence>))]
public enum RulePrecedence
{
    PriorityThenDeny,
}