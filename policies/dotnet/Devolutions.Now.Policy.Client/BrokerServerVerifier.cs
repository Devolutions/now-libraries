namespace Devolutions.Now.Policy.Client;

/// <summary>State of a Windows service as reported by the service control manager.</summary>
internal sealed record BrokerServiceInfo(bool IsRunning, int ProcessId, string? AccountName);

/// <summary>Queries used to verify the package broker pipe server.</summary>
internal interface IBrokerServerInspector
{
    /// <summary>Return the state of <paramref name="serviceName"/>, or <c>null</c> when it is not installed.</summary>
    BrokerServiceInfo? QueryService(string serviceName);

    /// <summary>Return the user SID of the server process token, or <c>null</c> when the token is not accessible.</summary>
    string? TryGetServerProcessUserSid();
}

/// <summary>Checks that the pipe server process is the package broker service running as LocalSystem.</summary>
internal static class BrokerServerVerifier
{
    internal const string LocalSystemSid = "S-1-5-18";

    private static readonly string[] LocalSystemAccountNames = ["LocalSystem", @".\LocalSystem", @"NT AUTHORITY\SYSTEM"];

    internal static void Verify(int serverProcessId, IReadOnlyList<string> serviceNames, IBrokerServerInspector inspector, string endpoint)
    {
        if (serverProcessId <= 0)
        {
            throw Failure("the pipe server process could not be determined", endpoint);
        }

        if (serviceNames.Count > 0)
        {
            VerifyServiceProcess(serverProcessId, serviceNames, inspector, endpoint);
        }

        var userSid = inspector.TryGetServerProcessUserSid();
        if (userSid is null)
        {
            if (serviceNames.Count == 0)
            {
                throw Failure("the pipe server process account could not be read", endpoint);
            }

            // The service configuration, which only administrators can change, already confirmed the account.
            return;
        }

        if (!string.Equals(userSid, LocalSystemSid, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("the pipe server process does not run as LocalSystem", endpoint);
        }
    }

    private static void VerifyServiceProcess(int serverProcessId, IReadOnlyList<string> serviceNames, IBrokerServerInspector inspector, string endpoint)
    {
        var anyInstalled = false;
        foreach (var serviceName in serviceNames)
        {
            var service = inspector.QueryService(serviceName);
            if (service is null)
            {
                continue;
            }

            anyInstalled = true;
            if (!service.IsRunning || service.ProcessId != serverProcessId)
            {
                continue;
            }

            if (!IsLocalSystemAccount(service.AccountName))
            {
                throw Failure($"the '{serviceName}' service is not configured to run as LocalSystem", endpoint);
            }

            return;
        }

        throw Failure(
            anyInstalled
                ? $"the pipe server process is not the running {FormatNames(serviceNames)} service process"
                : $"no {FormatNames(serviceNames)} service is installed",
            endpoint);
    }

    internal static bool IsLocalSystemAccount(string? accountName) =>
        accountName is not null && LocalSystemAccountNames.Contains(accountName, StringComparer.OrdinalIgnoreCase);

    private static string FormatNames(IReadOnlyList<string> serviceNames) =>
        string.Join(" or ", serviceNames.Select(name => $"'{name}'"));

    internal static BrokerClientException Failure(string reason, string endpoint, Exception? innerException = null) =>
        new(
            BrokerClientErrorKind.ServerVerificationFailed,
            $"The package broker pipe server could not be verified for {endpoint}: {reason}.",
            endpoint,
            innerException: innerException);
}