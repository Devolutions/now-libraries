using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient
{
    /// <summary>
    /// Delegate for handling RDM application notifications.
    /// </summary>
    /// <param name="appState">The RDM application state.</param>
    /// <param name="reasonCode">The reason code for the state change.</param>
    /// <param name="notifyData">Additional notification data.</param>
    public delegate void RdmAppNotifyHandler(NowRdmAppState appState, NowRdmReason reasonCode, string notifyData);

    /// <summary>
    /// Delegate for handling RDM session notifications.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="sessionNotify">The type of session notification.</param>
    /// <param name="logData">Session log data.</param>
    public delegate void RdmSessionNotifyHandler(Guid sessionId, NowRdmSessionNotifyKind sessionNotify, string logData);

    /// <summary>
    /// RDM version information.
    /// </summary>
    /// <param name="version">The RDM version string (e.g., "2025.1.0.0").</param>
    /// <param name="extra">Extra version information.</param>
    public class RdmVersion(string version, string? extra)
    {
        /// <summary>
        /// Gets the RDM version string (e.g., "2025.1.0.0").
        /// </summary>
        public string Version { get; } = version;

        /// <summary>
        /// Gets extra version information.
        /// </summary>
        public string? Extra { get; } = extra;
    }

    /// <summary>
    /// Contains information about RDM capabilities and version.
    /// </summary>
    /// <param name="isAppAvailable">A value indicating whether RDM application is available.</param>
    /// <param name="rdmVersion">The RDM version string (e.g., "2025.1.0.0").</param>
    /// <param name="versionExtra">Extra version information.</param>
    /// <param name="serverTimestamp">The server timestamp from the capabilities exchange.</param>
    internal class RdmCapabilityInfo(bool isAppAvailable, string rdmVersion, string versionExtra, DateTimeOffset serverTimestamp)
    {
        /// <summary>
        /// Gets a value indicating whether RDM application is available.
        /// </summary>
        public bool IsAppAvailable { get; } = isAppAvailable;

        /// <summary>
        /// Gets the RDM version string (e.g., "2025.1.0.0").
        /// </summary>
        public string RdmVersion { get; } = rdmVersion;

        /// <summary>
        /// Gets extra version information.
        /// </summary>
        public string VersionExtra { get; } = versionExtra;

        /// <summary>
        /// Gets the server timestamp from the capabilities exchange.
        /// </summary>
        public DateTimeOffset ServerTimestamp { get; } = serverTimestamp;
    }

    /// <summary>
    /// Parameters for starting RDM application.
    /// </summary>
    public class RdmStartParams
    {
        /// <summary>
        /// Sets the launch flags for the RDM application.
        /// </summary>
        public RdmStartParams LaunchFlags(NowRdmLaunchFlags flags)
        {
            _launchFlags = flags;
            return this;
        }

        /// <summary>
        /// Sets the timeout for RDM to launch and become ready.
        /// </summary>
        public RdmStartParams Timeout(TimeSpan timeout)
        {
            _timeout = timeout;
            return this;
        }

        internal NowRdmLaunchFlags GetLaunchFlags() => _launchFlags;
        internal TimeSpan GetTimeout() => _timeout;

        private NowRdmLaunchFlags _launchFlags = NowRdmLaunchFlags.None;
        private TimeSpan _timeout = TimeSpan.FromSeconds(45);
    }

    /// <summary>
    /// Parameters for starting an RDM session.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="connectionData">The serialized RDM XML connection object.</param>
    public class RdmSessionStartParams(Guid sessionId, Guid connectionId, string connectionData)
    {
        /// <summary>
        /// Gets the unique session identifier.
        /// </summary>
        public Guid SessionId { get; } = sessionId;

        /// <summary>
        /// Gets the connection identifier.
        /// </summary>
        public Guid ConnectionId { get; } = connectionId;

        /// <summary>
        /// Gets the serialized RDM XML connection object.
        /// </summary>
        public string ConnectionData { get; } = connectionData;
    }
}