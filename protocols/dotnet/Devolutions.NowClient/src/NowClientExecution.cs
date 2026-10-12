using Devolutions.NowClient.Execution;

namespace Devolutions.NowClient
{
    /// <summary>
    /// Owns a dedicated thread for asynchronous NOW client operations and monotonic delays.
    /// </summary>
    /// <remarks>
    /// Operations and their captured continuations share this thread and must not block it.
    /// Avoid escaping the context with ConfigureAwait(false) or Task.Run when thread affinity
    /// is required. Clients borrowing this execution do not own it: stop or cancel those
    /// clients and any other outstanding operations before awaiting disposal.
    /// </remarks>
    public sealed class NowClientExecution : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly DedicatedExecution _pump = new();
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
        /// A delay admitted before disposal keeps the thread alive until it expires or is
        /// canceled. Disposal does not cancel it, including an infinite delay.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">Disposal has already begun.</exception>
        public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            return Run(() => _pump.Delay(delay, cancellationToken));
        }

        /// <summary>
        /// Rejects new operations and asynchronously waits for admitted operations and thread cleanup.
        /// </summary>
        /// <remarks>
        /// Does not cancel clients, delays or arbitrary external tasks awaited by operations.
        /// Stop or cancel them before awaiting this method. Calling this method from an operation
        /// is supported, but do not await its completion within that operation: disposal must wait
        /// for the operation itself to return. Repeated calls observe the same completion.
        /// </remarks>
        public ValueTask DisposeAsync()
        {
            bool stop;
            lock (_gate)
            {
                _disposing = true;
                stop = _activeOperations == 0;
            }

            if (stop)
            {
                _stop.TrySetResult();
            }

            return new ValueTask(_pump.Completion);
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