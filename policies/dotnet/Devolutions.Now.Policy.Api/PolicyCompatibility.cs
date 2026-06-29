using PolicyArchitecture = Devolutions.Now.Policy.Model.Architecture;
using PolicyDecision = Devolutions.Now.Policy.Model.Decision;
using PolicyElevation = Devolutions.Now.Policy.Model.Elevation;
using PolicyManagerName = Devolutions.Now.Policy.Model.ManagerName;
using PolicyOperation = Devolutions.Now.Policy.Model.Operation;
using PolicyScope = Devolutions.Now.Policy.Model.Scope;

namespace Devolutions.Now.Policy.Api;

/// <summary>Conversions between package broker API DTOs and policy model DTOs.</summary>
public static class PolicyCompatibility
{
    public static PolicyOperation ToPolicyModel(this Operation value) => value switch
    {
        Operation.Install => PolicyOperation.Install,
        Operation.Update => PolicyOperation.Update,
        Operation.Uninstall => PolicyOperation.Uninstall,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static Operation ToPackageBrokerApi(this PolicyOperation value) => value switch
    {
        PolicyOperation.Install => Operation.Install,
        PolicyOperation.Update => Operation.Update,
        PolicyOperation.Uninstall => Operation.Uninstall,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static PolicyManagerName ToPolicyModel(this ManagerName value) => value switch
    {
        ManagerName.Winget => PolicyManagerName.Winget,
        ManagerName.PowerShell => PolicyManagerName.PowerShell,
        ManagerName.PowerShell7 => PolicyManagerName.PowerShell7,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static ManagerName ToPackageBrokerApi(this PolicyManagerName value) => value switch
    {
        PolicyManagerName.Winget => ManagerName.Winget,
        PolicyManagerName.PowerShell => ManagerName.PowerShell,
        PolicyManagerName.PowerShell7 => ManagerName.PowerShell7,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static PolicyScope ToPolicyModel(this Scope value) => value switch
    {
        Scope.User => PolicyScope.User,
        Scope.Machine => PolicyScope.Machine,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static Scope ToPackageBrokerApi(this PolicyScope value) => value switch
    {
        PolicyScope.User => Scope.User,
        PolicyScope.Machine => Scope.Machine,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static PolicyArchitecture ToPolicyModel(this Architecture value) => value switch
    {
        Architecture.X86 => PolicyArchitecture.X86,
        Architecture.X64 => PolicyArchitecture.X64,
        Architecture.Arm64 => PolicyArchitecture.Arm64,
        Architecture.Neutral => PolicyArchitecture.Neutral,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static Architecture ToPackageBrokerApi(this PolicyArchitecture value) => value switch
    {
        PolicyArchitecture.X86 => Architecture.X86,
        PolicyArchitecture.X64 => Architecture.X64,
        PolicyArchitecture.Arm64 => Architecture.Arm64,
        PolicyArchitecture.Neutral => Architecture.Neutral,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static PolicyElevation ToPolicyModel(this Elevation value) => value switch
    {
        Elevation.Standard => PolicyElevation.Standard,
        Elevation.Elevated => PolicyElevation.Elevated,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static Elevation ToPackageBrokerApi(this PolicyElevation value) => value switch
    {
        PolicyElevation.Standard => Elevation.Standard,
        PolicyElevation.Elevated => Elevation.Elevated,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static PolicyDecision ToPolicyModel(this Decision value) => value switch
    {
        Decision.Allow => PolicyDecision.Allow,
        Decision.Deny => PolicyDecision.Deny,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static Decision ToPackageBrokerApi(this PolicyDecision value) => value switch
    {
        PolicyDecision.Allow => Decision.Allow,
        PolicyDecision.Deny => Decision.Deny,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}