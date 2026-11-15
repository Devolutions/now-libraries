using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace Devolutions.Now.Policy.Client;

/// <summary>Reads the server process and session of a connected client pipe.</summary>
internal static class PipeServerIdentityReader
{
    internal static BrokerPipeServerIdentity Read(NamedPipeClientStream pipe)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Pipe server identity is available only on Windows.");
        }

        var handle = pipe.SafePipeHandle;
        if (!GetNamedPipeServerProcessId(handle, out var processId))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!GetNamedPipeServerSessionId(handle, out var sessionId))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new BrokerPipeServerIdentity(checked((int)processId), checked((int)sessionId));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out uint serverSessionId);
}