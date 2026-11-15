namespace Devolutions.Now.Policy.Client;

/// <summary>Process and session of the server end of a connected broker pipe, as reported by the operating system.</summary>
public readonly record struct BrokerPipeServerIdentity(int ProcessId, int SessionId);

/// <summary>
/// Validates the server identity reported by a connected pipe.
/// The client calls it after connecting and before writing any byte, including the grant.
/// </summary>
public interface IBrokerServerAuthenticator
{
    /// <summary>Throw to reject <paramref name="server"/>; returning accepts it.</summary>
    void Authenticate(BrokerPipeServerIdentity server);
}