using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

using Microsoft.Win32.SafeHandles;

namespace Devolutions.Now.Policy.Client;

/// <summary>Windows queries for <see cref="BrokerServerVerifier"/>, bound to one held server process handle.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsBrokerServerInspector(SafeProcessHandle? serverProcess) : IBrokerServerInspector
{
    public BrokerServiceInfo? QueryService(string serviceName)
    {
        using var manager = NativeMethods.OpenSCManagerW(null, null, NativeMethods.SC_MANAGER_CONNECT);
        if (manager.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        using var service = NativeMethods.OpenServiceW(
            manager,
            serviceName,
            NativeMethods.SERVICE_QUERY_STATUS | NativeMethods.SERVICE_QUERY_CONFIG);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == NativeMethods.ERROR_SERVICE_DOES_NOT_EXIST)
            {
                return null;
            }

            throw new Win32Exception(error);
        }

        var status = QueryStatus(service);
        return new BrokerServiceInfo(
            status.dwCurrentState == NativeMethods.SERVICE_RUNNING,
            unchecked((int)status.dwProcessId),
            QueryAccountName(service));
    }

    public string? TryGetServerProcessUserSid()
    {
        if (serverProcess is null)
        {
            return null;
        }

        if (!NativeMethods.OpenProcessToken(serverProcess, NativeMethods.TOKEN_QUERY, out var token))
        {
            var error = Marshal.GetLastPInvokeError();
            token.Dispose();
            if (error == NativeMethods.ERROR_ACCESS_DENIED)
            {
                return null;
            }

            throw new Win32Exception(error);
        }

        using (token)
        {
            NativeMethods.GetTokenInformation(token, NativeMethods.TokenUser, IntPtr.Zero, 0, out var length);
            if (length == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenUser, buffer, length, out _))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                // TOKEN_USER starts with SID_AND_ATTRIBUTES, whose first field is the SID pointer.
                var sid = Marshal.ReadIntPtr(buffer);
                return new SecurityIdentifier(sid).Value;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static unsafe NativeMethods.SERVICE_STATUS_PROCESS QueryStatus(SafeServiceHandle service)
    {
        NativeMethods.SERVICE_STATUS_PROCESS status;
        if (!NativeMethods.QueryServiceStatusEx(
                service,
                NativeMethods.SC_STATUS_PROCESS_INFO,
                &status,
                (uint)sizeof(NativeMethods.SERVICE_STATUS_PROCESS),
                out _))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return status;
    }

    private static unsafe string? QueryAccountName(SafeServiceHandle service)
    {
        NativeMethods.QueryServiceConfigW(service, null, 0, out var needed);
        var error = Marshal.GetLastPInvokeError();
        if (error != NativeMethods.ERROR_INSUFFICIENT_BUFFER || needed == 0)
        {
            throw new Win32Exception(error);
        }

        var buffer = (NativeMethods.QUERY_SERVICE_CONFIGW*)NativeMemory.Alloc(needed);
        try
        {
            if (!NativeMethods.QueryServiceConfigW(service, buffer, needed, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return Marshal.PtrToStringUni(buffer->lpServiceStartName);
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    /// <summary>Return the process id of the server of a connected pipe.</summary>
    internal static int GetServerProcessId(SafePipeHandle pipe)
    {
        if (!NativeMethods.GetNamedPipeServerProcessId(pipe, out var processId))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return unchecked((int)processId);
    }

    /// <summary>
    /// Open a handle to <paramref name="processId"/> that keeps the id from being reused while held, or return
    /// <c>null</c> when the caller is not allowed to open the process (non-elevated callers and LocalSystem services).
    /// </summary>
    internal static SafeProcessHandle? TryOpenServerProcess(int processId)
    {
        var process = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            unchecked((uint)processId));
        if (process.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            process.Dispose();
            if (error == NativeMethods.ERROR_ACCESS_DENIED)
            {
                return null;
            }

            throw new Win32Exception(error);
        }

        if (NativeMethods.GetProcessId(process) != unchecked((uint)processId))
        {
            process.Dispose();
            throw new Win32Exception(NativeMethods.ERROR_INVALID_HANDLE);
        }

        return process;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeServiceHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseServiceHandle(handle);
}

[SupportedOSPlatform("windows")]
internal static unsafe partial class NativeMethods
{
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_INVALID_HANDLE = 6;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;

    internal const uint SC_MANAGER_CONNECT = 0x0001;
    internal const uint SERVICE_QUERY_CONFIG = 0x0001;
    internal const uint SERVICE_QUERY_STATUS = 0x0004;
    internal const int SC_STATUS_PROCESS_INFO = 0;
    internal const uint SERVICE_RUNNING = 0x00000004;

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const int TokenUser = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct QUERY_SERVICE_CONFIGW
    {
        public uint dwServiceType;
        public uint dwStartType;
        public uint dwErrorControl;
        public IntPtr lpBinaryPathName;
        public IntPtr lpLoadOrderGroup;
        public uint dwTagId;
        public IntPtr lpDependencies;
        public IntPtr lpServiceStartName;
        public IntPtr lpDisplayName;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint GetProcessId(SafeProcessHandle process);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(SafeProcessHandle process, uint desiredAccess, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(
        SafeAccessTokenHandle token,
        int tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeServiceHandle OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeServiceHandle OpenServiceW(SafeServiceHandle manager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryServiceStatusEx(
        SafeServiceHandle service,
        int infoLevel,
        SERVICE_STATUS_PROCESS* buffer,
        uint bufferSize,
        out uint bytesNeeded);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryServiceConfigW(
        SafeServiceHandle service,
        QUERY_SERVICE_CONFIGW* serviceConfig,
        uint bufferSize,
        out uint bytesNeeded);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseServiceHandle(IntPtr handle);
}