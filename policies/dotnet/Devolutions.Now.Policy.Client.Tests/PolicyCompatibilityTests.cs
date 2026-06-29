using Xunit;

using PolicyArchitecture = Devolutions.Now.Policy.Model.Architecture;
using PolicyDecision = Devolutions.Now.Policy.Model.Decision;
using PolicyElevation = Devolutions.Now.Policy.Model.Elevation;
using PolicyManagerName = Devolutions.Now.Policy.Model.ManagerName;
using PolicyOperation = Devolutions.Now.Policy.Model.Operation;
using PolicyScope = Devolutions.Now.Policy.Model.Scope;

namespace Devolutions.Now.Policy.Client.Tests;

public sealed class PolicyCompatibilityTests
{
    [Fact]
    public void OperationRoundTrips()
    {
        Assert.Equal(Operation.Install, PolicyOperation.Install.ToPackageBrokerApi());
        Assert.Equal(Operation.Update, PolicyOperation.Update.ToPackageBrokerApi());
        Assert.Equal(Operation.Uninstall, PolicyOperation.Uninstall.ToPackageBrokerApi());

        Assert.Equal(PolicyOperation.Install, Operation.Install.ToPolicyModel());
        Assert.Equal(PolicyOperation.Update, Operation.Update.ToPolicyModel());
        Assert.Equal(PolicyOperation.Uninstall, Operation.Uninstall.ToPolicyModel());
    }

    [Fact]
    public void ManagerNameRoundTrips()
    {
        Assert.Equal(ManagerName.Winget, PolicyManagerName.Winget.ToPackageBrokerApi());
        Assert.Equal(ManagerName.PowerShell, PolicyManagerName.PowerShell.ToPackageBrokerApi());
        Assert.Equal(ManagerName.PowerShell7, PolicyManagerName.PowerShell7.ToPackageBrokerApi());

        Assert.Equal(PolicyManagerName.Winget, ManagerName.Winget.ToPolicyModel());
        Assert.Equal(PolicyManagerName.PowerShell, ManagerName.PowerShell.ToPolicyModel());
        Assert.Equal(PolicyManagerName.PowerShell7, ManagerName.PowerShell7.ToPolicyModel());
    }

    [Fact]
    public void ScopeRoundTrips()
    {
        Assert.Equal(Scope.User, PolicyScope.User.ToPackageBrokerApi());
        Assert.Equal(Scope.Machine, PolicyScope.Machine.ToPackageBrokerApi());

        Assert.Equal(PolicyScope.User, Scope.User.ToPolicyModel());
        Assert.Equal(PolicyScope.Machine, Scope.Machine.ToPolicyModel());
    }

    [Fact]
    public void ArchitectureRoundTrips()
    {
        Assert.Equal(Architecture.X86, PolicyArchitecture.X86.ToPackageBrokerApi());
        Assert.Equal(Architecture.X64, PolicyArchitecture.X64.ToPackageBrokerApi());
        Assert.Equal(Architecture.Arm64, PolicyArchitecture.Arm64.ToPackageBrokerApi());
        Assert.Equal(Architecture.Neutral, PolicyArchitecture.Neutral.ToPackageBrokerApi());

        Assert.Equal(PolicyArchitecture.X86, Architecture.X86.ToPolicyModel());
        Assert.Equal(PolicyArchitecture.X64, Architecture.X64.ToPolicyModel());
        Assert.Equal(PolicyArchitecture.Arm64, Architecture.Arm64.ToPolicyModel());
        Assert.Equal(PolicyArchitecture.Neutral, Architecture.Neutral.ToPolicyModel());
    }

    [Fact]
    public void ElevationRoundTrips()
    {
        Assert.Equal(Elevation.Standard, PolicyElevation.Standard.ToPackageBrokerApi());
        Assert.Equal(Elevation.Elevated, PolicyElevation.Elevated.ToPackageBrokerApi());

        Assert.Equal(PolicyElevation.Standard, Elevation.Standard.ToPolicyModel());
        Assert.Equal(PolicyElevation.Elevated, Elevation.Elevated.ToPolicyModel());
    }

    [Fact]
    public void DecisionRoundTrips()
    {
        Assert.Equal(Decision.Allow, PolicyDecision.Allow.ToPackageBrokerApi());
        Assert.Equal(Decision.Deny, PolicyDecision.Deny.ToPackageBrokerApi());

        Assert.Equal(PolicyDecision.Allow, Decision.Allow.ToPolicyModel());
        Assert.Equal(PolicyDecision.Deny, Decision.Deny.ToPolicyModel());
    }
}