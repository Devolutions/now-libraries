namespace Devolutions.NowClient
{
    /// <summary>
    /// Controls connection setup and protocol execution. Existing callers use the thread pool.
    /// </summary>
    public sealed class NowClientOptions
    {
        /// <summary>
        /// Where protocol work runs. Consumer continuations retain their usual scheduling regardless.
        /// </summary>
        public NowClientScheduling Scheduling { get; init; } = NowClientScheduling.ThreadPool;

        /// <summary>
        /// Maximum time for the initial capability exchange, including the initial write.
        /// </summary>
        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// The heartbeat interval requested from the server (NOW_CHANNEL_SET_HEARTBEAT, 5 s to 24 h). The worker
        /// enforces the smaller of this and the server's advertised interval.
        /// </summary>
        public TimeSpan RequestedHeartbeatInterval { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// What to do when the server's capset carries no heartbeat interval. A server that heartbeats
        /// periodically advertises its interval, so the default lets the server decide; enforcing the
        /// client's own request against a server that only heartbeats when idle drops busy connections.
        /// </summary>
        public NowClientHeartbeatPolicy UnspecifiedPeerHeartbeat { get; init; } = NowClientHeartbeatPolicy.FollowPeer;
    }

    /// <summary>
    /// How the client treats a server capset without a heartbeat interval.
    /// </summary>
    public enum NowClientHeartbeatPolicy
    {
        /// <summary>
        /// No interval means no watchdog, as if the server had sent zero. A specified interval is still
        /// honoured (the smaller of the client's request and the server's value).
        /// </summary>
        FollowPeer,

        /// <summary>
        /// Enforce the client's requested interval. Only for servers known to heartbeat periodically
        /// without advertising it; any received message counts as liveness, writes do not.
        /// </summary>
        EnforceRequested,
    }
}