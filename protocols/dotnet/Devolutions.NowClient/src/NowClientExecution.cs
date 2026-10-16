using Devolutions.NowClient.Execution;

namespace Devolutions.NowClient
{
    /// <summary>
    /// Owns a dedicated thread for asynchronous NOW client operations and monotonic delays.
    /// </summary>
    /// <remarks>
    /// Operations and their captured continuations share this thread and must not block it.
    /// Avoid escaping the context with ConfigureAwait(false) or Task.Run when thread affinity
    /// is required. Disposal stops every client still attached to this execution and cancels
    /// its pending delays, then waits for admitted operations to finish. Unrelated external
    /// tasks must be completed or canceled by their callers.
    /// </remarks>
    public sealed class NowClientExecution : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly DedicatedExecution _pump = new();
        private readonly HashSet<ClientLifetime> _clients = new();
        // The private root captures the pump; completing it must not require the thread pool.
        private readonly TaskCompletionSource _stop = new();
        private int _activeOperations;
        private bool _disposing;

        /// <summary>
        /// Starts the owned execution thread. Thread-start failures propagate to the caller.
        /// </summary>
        public NowClientExecution()
        {
            _pump.Start(async () => await _stop.Task);
        }

        internal DedicatedExecution Context => _pump;

        /// <summary>
        /// Completes when the owned execution thread has finished dispatching and cleaned up.
        /// Individual operation failures are reported by their returned tasks instead.
        /// </summary>
        public Task Completion => _pump.Completion;

        /// <summary>
        /// Admits an operation and runs its entire asynchronous lifetime on the owned context.
        /// </summary>
        /// <remarks>
        /// May be called from any thread, including a nested operation before disposal begins.
        /// An admitted operation still executes if disposal begins before it is dispatched.
        /// Exceptions and cancellation are reported through the returned task without
        /// stopping the execution thread or inlining consumer continuations on completion.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">Disposal has already begun.</exception>
        public Task Run(Func<Task> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            return Run(async () =>
            {
                await operation();
                return true;
            });
        }

        /// <summary>
        /// Admits an operation and returns its result after its entire asynchronous lifetime.
        /// </summary>
        /// <remarks>
        /// Runs on the owned context with the same admission, nesting and completion guarantees
        /// as <see cref="Run(Func{Task})"/>. Await asynchronous work inside the delegate so that
        /// disposal accounts for it; detached work is not tracked.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">Disposal has already begun.</exception>
        public Task<T> Run<T>(Func<Task<T>> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposing, this);
                _activeOperations++;
            }

            _ = ExecuteAsync(operation, completion);
            return completion.Task;
        }

        /// <summary>
        /// Runs a tracked, cancellable monotonic delay on the owned context without pool timers.
        /// May be called from any thread.
        /// </summary>
        /// <remarks>
        /// Disposal cancels pending delays, including those admitted but not yet dispatched.
        /// The returned task may be canceled while the supplied token remains uncanceled.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">Disposal has already begun.</exception>
        public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            return Run(() => _pump.Delay(delay, cancellationToken));
        }

        /// <summary>
        /// Rejects new operations, stops every attached client, cancels pending delays and
        /// asynchronously waits for admitted operations and thread cleanup.
        /// </summary>
        /// <remarks>
        /// Stopping a client is a request: its worker still drains on this thread, so the
        /// returned task completes only once those workers have finished. An operation awaiting
        /// an external task that is neither a client nor a delay is not interrupted. Calling
        /// this method from an operation is supported, but do not await its completion within
        /// that operation: disposal must wait for the operation itself to return. Repeated
        /// calls observe the same completion.
        /// </remarks>
        public ValueTask DisposeAsync()
        {
            bool stop;
            ClientLifetime[] clients;
            lock (_gate)
            {
                if (_disposing)
                {
                    return new ValueTask(_pump.Completion);
                }

                _disposing = true;
                clients = _clients.ToArray();
                _clients.Clear();
                stop = _activeOperations == 0;
            }

            foreach (ClientLifetime client in clients)
            {
                client.RequestStop();
            }

            // Client tokens must be canceled before protocol waits can observe canceled timers.
            _pump.TryPost(_pump.CancelPendingDelays);

            if (stop)
            {
                _stop.TrySetResult();
            }

            return new ValueTask(_pump.Completion);
        }

        // Register before admission so disposal cannot miss a worker admitted concurrently.
        internal void Attach(ClientLifetime client)
        {
            lock (_gate)
            {
                _clients.Add(client);
            }
        }

        internal void Detach(ClientLifetime client)
        {
            lock (_gate)
            {
                _clients.Remove(client);
            }
        }

        private async Task ExecuteAsync<T>(Func<Task<T>> operation, TaskCompletionSource<T> completion)
        {
            try
            {
                await _pump.Enter();
                T result = await operation();
                completion.TrySetResult(result);
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                bool stop;
                lock (_gate)
                {
                    _activeOperations--;
                    stop = _disposing && _activeOperations == 0;
                }

                if (stop)
                {
                    _stop.TrySetResult();
                }
            }
        }
    }
}