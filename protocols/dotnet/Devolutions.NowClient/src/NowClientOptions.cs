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
    }
}