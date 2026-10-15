namespace Devolutions.NowClient
{
    /// <summary>
    /// Selects where a connection's protocol work (negotiation, worker, callbacks and timers) runs.
    /// </summary>
    public sealed class NowClientScheduling
    {
        private NowClientScheduling(NowClientExecution? execution, bool ownsThread)
        {
            Execution = execution;
            OwnsThread = ownsThread;
        }

        /// <summary>
        /// The thread pool, as for <see cref="NowClient.Connect(INowTransport)"/>.
        /// </summary>
        public static NowClientScheduling ThreadPool { get; } = new(null, false);

        /// <summary>
        /// One background thread owned by the connection. Transport operations and callbacks must not
        /// block it or require the thread pool to produce their completions.
        /// </summary>
        public static NowClientScheduling DedicatedThread { get; } = new(null, true);

        /// <summary>
        /// A caller-owned thread shared with other connections and with application work run through
        /// <see cref="NowClientExecution.Run{T}"/>. Disposing the execution stops the connection.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="execution"/> is null.</exception>
        public static NowClientScheduling SharedThread(NowClientExecution execution)
        {
            ArgumentNullException.ThrowIfNull(execution);
            return new(execution, false);
        }

        internal NowClientExecution? Execution { get; }

        internal bool OwnsThread { get; }
    }
}