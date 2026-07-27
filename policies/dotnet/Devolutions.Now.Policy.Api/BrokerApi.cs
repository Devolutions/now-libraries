using System.Text.Json;

namespace Devolutions.Now.Policy.Api;

/// <summary>Constants shared by package broker API clients and implementations.</summary>
public static class BrokerApi
{
    public const string Version = "1.1";

    public const string DefaultPipeName = "Devolutions.Now.PackageBroker.v1";

    public const string PackageRequestKind = "PackageRequest";
    public const string StatusRequestKind = "StatusRequest";

    public const string HealthResponseKind = "HealthResponse";
    public const string CapabilitiesResponseKind = "CapabilitiesResponse";
    public const string EvaluationResponseKind = "EvaluationResponse";
    public const string ExecutionResponseKind = "ExecutionResponse";
    public const string StatusResponseKind = "StatusResponse";
    public const string ErrorResponseKind = "ErrorResponse";

    internal static string ValidateMessageKind(string? value, string expected, string propertyName)
    {
        if (value == expected)
        {
            return value;
        }

        throw new JsonException($"{propertyName} must be '{expected}', but was '{value ?? "<null>"}'.");
    }

    /// <summary>
    /// Minimum broker API version that understands the given package manager name.
    /// Clients must not send a manager to a broker whose advertised API version is
    /// older than this value: the older broker would reject the unknown enum value
    /// as a validation failure instead of reporting a meaningful capability error.
    /// </summary>
    public static string GetMinimumApiVersion(this ManagerName manager) => manager switch
    {
        ManagerName.Winget or ManagerName.PowerShell or ManagerName.PowerShell7 => "1.0",
        ManagerName.Apt
            or ManagerName.Bun
            or ManagerName.Cargo
            or ManagerName.Chocolatey
            or ManagerName.Dnf
            or ManagerName.Dotnet
            or ManagerName.Flatpak
            or ManagerName.Homebrew
            or ManagerName.Npm
            or ManagerName.Pacman
            or ManagerName.Pip
            or ManagerName.Scoop
            or ManagerName.Snap
            or ManagerName.Vcpkg => "1.1",
        _ => throw new ArgumentOutOfRangeException(nameof(manager), manager, null),
    };

    /// <summary>
    /// Whether API version <paramref name="version"/> (e.g. a broker's advertised
    /// version) supports a feature introduced in <paramref name="required"/>
    /// (same major version, minor at least as new).
    /// </summary>
    public static bool ApiVersionSupports(string version, string required)
    {
        return TryParseApiVersion(version, out var major, out var minor)
            && TryParseApiVersion(required, out var requiredMajor, out var requiredMinor)
            && major == requiredMajor
            && minor >= requiredMinor;
    }

    private static bool TryParseApiVersion(string value, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        var separator = value.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        return int.TryParse(value.AsSpan(0, separator), out major)
            && int.TryParse(value.AsSpan(separator + 1), out minor);
    }
}