using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Devolutions.Now.Policy.Client;

/// <summary>
/// Compares the pipe server's PID and session to the running Windows service with a fixed name.
/// This check alone does not establish a live service instance if the PID is reused.
/// Off Windows every server is rejected.
/// </summary>
public sealed class ServiceBrokerServerAuthenticator : IBrokerServerAuthenticator
{
    public const string DefaultServiceName = "DevolutionsAgent";

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ScStatusProcessInfo = 0;
    private const uint ServiceRunning = 4;

    private readonly string _serviceName;
    private readonly Func<string, (uint State, uint ProcessId)> _queryService;

    public ServiceBrokerServerAuthenticator(string serviceName = DefaultServiceName)
        : this(serviceName, QueryService)
    {
    }

    internal ServiceBrokerServerAuthenticator(string serviceName, Func<string, (uint State, uint ProcessId)> queryService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        _serviceName = serviceName;
        _queryService = queryService;
    }

    public void Authenticate(BrokerPipeServerIdentity server)
    {
        if (server.SessionId != 0)
        {
            throw new InvalidOperationException("The pipe server does not run in session 0.");
        }

        var (state, processId) = _queryService(_serviceName);
        if (state != ServiceRunning)
        {
            throw new InvalidOperationException($"The service '{_serviceName}' is not running.");
        }

        if (processId == 0 || processId != (uint)server.ProcessId)
        {
            throw new InvalidOperationException($"The pipe server is not the process of service '{_serviceName}'.");
        }
    }

    // UNTESTED: the service query must report the PID of the real DevolutionsAgent service to a
    // standard-user caller, and the PID check is racy if the service exits and its PID is reused.
    // Tests only fake the query; when changing this, check it against an installed Agent service.
    private static (uint State, uint ProcessId) QueryService(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Service-based server authentication requires Windows.");
        }

        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var service = OpenService(manager, serviceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                if (!QueryServiceStatusEx(service, ScStatusProcessInfo, out var status, (uint)Marshal.SizeOf<ServiceStatusProcess>(), out _))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return (status.CurrentState, status.ProcessId);
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        IntPtr service,
        int infoLevel,
        out ServiceStatusProcess buffer,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}