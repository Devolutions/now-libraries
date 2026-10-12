namespace Devolutions.NowClient
{
    /// <summary>
    /// Controls connection setup and protocol execution. Existing callers use the thread pool.
    /// </summary>
    public sealed class NowClientOptions
    {
        /// <summary>
        /// Own a dedicated protocol thread, including negotiation, async continuations and timers.
        /// Transport operations and callbacks must not block this thread or require the thread pool
        /// to produce their completions. Consumer continuations retain their usual scheduling.
        /// </summary>
        public bool UseDedicatedThread { get; init; }

        /// <summary>
        /// Borrows a caller-owned execution context for the entire connection. Do not also
        /// set UseDedicatedThread. Stop the client before disposing its borrowed execution.
        /// </summary>
        public NowClientExecution? Execution { get; init; }

        /// <summary>
        /// Maximum time for the initial capability exchange, including the initial write.
        /// </summary>
        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    }
}