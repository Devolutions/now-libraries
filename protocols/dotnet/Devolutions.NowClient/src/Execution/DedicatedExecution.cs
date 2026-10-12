using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Devolutions.NowClient.Execution
{
    /// <summary>
    /// Runs one asynchronous root and its captured continuations on an owned thread.
    /// The root must cover the entire worker lifetime; callbacks must not block the pump.
    /// Shutdown rejects pending entries and drains already-queued await continuations.
    /// ConfigureAwait(false), Task.Run and external I/O implementations remain outside
    /// this context's scheduling guarantees.
    /// </summary>
    internal sealed class DedicatedExecution : SynchronizationContext
    {
        private readonly object _gate = new();
        private readonly Queue<WorkItem> _callbacks = new();
        private readonly Queue<DelayRegistration> _canceledTimers = new();
        private readonly SortedSet<DelayRegistration> _timers = new(
            Comparer<DelayRegistration>.Create(static (left, right) =>
            {
                int comparison = left.Deadline.CompareTo(right.Deadline);
                return comparison != 0 ? comparison : left.Sequence.CompareTo(right.Sequence);
            }));
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread _thread;
        private Func<Task>? _run;
        private Task? _root;
        private bool _started;
        private bool _stopping;
        private bool _closed;
        private ulong _nextTimerSequence;

        public DedicatedExecution()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "NOW client execution",
            };
        }

        // Set only after dispatch has ended, timers/registrations have been cleaned,
        // and queued entries have been rejected. Consumer continuations cannot inline.
        public Task Completion => _completion.Task;

        public bool IsCurrent => Thread.CurrentThread == _thread;

        internal int PendingTimerCount
        {
            get
            {
                lock (_gate)
                {
                    return _timers.Count + _canceledTimers.Count;
                }
            }
        }

        public void Start(Func<Task> run)
        {
            ArgumentNullException.ThrowIfNull(run);

            lock (_gate)
            {
                if (_started)
                {
                    throw new InvalidOperationException("The execution thread has already been started.");
                }

                _started = true;
                _run = run;
            }

            try
            {
                _thread.Start();
            }
            catch (Exception exception)
            {
                _run = null;
                List<Exception> failures = [exception];
                Stop(failures);
                Complete(null, failures);
                throw;
            }
        }

        public bool TryPost(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            // Acceptance does not keep the root alive or guarantee execution if it exits.
            return TryEnqueue(new WorkItem(action, isEntry: false));
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);

            // A transport can finish an outstanding await after the root has exited.
            // Such continuations must neither escape to the pool nor throw from Post.
            TryPost(() => callback(state));
        }

        public override void Send(SendOrPostCallback callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            if (!IsCurrent)
            {
                throw new NotSupportedException("Use Enter for asynchronous dispatch to the execution thread.");
            }

            GetEnterResult();
            Invoke(() => callback(state));
        }

        public override SynchronizationContext CreateCopy() => this;

        public ExecutionAwaitable Enter() => new(this);

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            if (!IsCurrent)
            {
                throw new InvalidOperationException("Delays must be created on the execution thread.");
            }

            lock (_gate)
            {
                ThrowIfClosed();
            }

            if (delay < TimeSpan.Zero && delay != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (delay == TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }

            // Int128 preserves sub-millisecond precision and accommodates TimeSpan.MaxValue.
            Int128 deadline = delay == Timeout.InfiniteTimeSpan
                ? Int128.MaxValue
                : (Int128)Stopwatch.GetTimestamp()
                    + (((Int128)delay.Ticks * Stopwatch.Frequency + TimeSpan.TicksPerSecond - 1)
                        / TimeSpan.TicksPerSecond);
            DelayRegistration timer = new(this, deadline, _nextTimerSequence++, cancellationToken);

            lock (_gate)
            {
                ThrowIfClosed();
                _timers.Add(timer);
            }

            // Only the owner assigns/disposes Registration. A concurrent cancellation
            // only removes the deadline and wakes the pump, so disposal never waits on
            // a callback which is itself waiting for the owner.
            timer.Registration = cancellationToken.UnsafeRegister(static state =>
            {
                DelayRegistration timer = (DelayRegistration)state!;
                timer.Owner.CancelTimer(timer);
            }, timer);

            return timer.Completion.Task;
        }

        internal void PostEntry(Action continuation)
        {
            ArgumentNullException.ThrowIfNull(continuation);
            if (!TryEnqueue(new WorkItem(continuation, isEntry: true)))
            {
                // The async state machine must resume to observe GetResult's exception.
                continuation();
            }
        }

        internal void GetEnterResult()
        {
            lock (_gate)
            {
                ThrowIfClosed();
            }

            if (!IsCurrent)
            {
                throw new InvalidOperationException("The continuation did not enter the execution thread.");
            }

            SetSynchronizationContext(this);
        }

        private bool IsClosed => _stopping || _root?.IsCompleted == true;

        private void ThrowIfClosed()
        {
            if (IsClosed)
            {
                throw new NowClientException("The client execution has terminated.");
            }
        }

        private bool TryEnqueue(WorkItem work)
        {
            lock (_gate)
            {
                if (_closed || (work.IsEntry && IsClosed))
                {
                    // This may be the root's completion notification while the pump
                    // is asleep. Even a rejected notification must wake it.
                    Monitor.Pulse(_gate);
                    return false;
                }

                _callbacks.Enqueue(work);
                Monitor.Pulse(_gate);
                return true;
            }
        }

        private void CancelTimer(DelayRegistration timer)
        {
            lock (_gate)
            {
                if (_timers.Remove(timer))
                {
                    _canceledTimers.Enqueue(timer);
                    Monitor.Pulse(_gate);
                }
            }
        }

        private void Run()
        {
            Task? root = null;
            List<Exception> failures = new();
            SetSynchronizationContext(this);

            try
            {
                Func<Task> run = _run!;
                _run = null;
                root = run() ?? throw new InvalidOperationException("The execution root returned no task.");
                lock (_gate)
                {
                    _root = root;
                }

                SetSynchronizationContext(this);
                if (!root.IsCompleted)
                {
                    // Capture this context, not TaskScheduler.Default. This wakes the
                    // pump even when the root is an externally completed task with
                    // RunContinuationsAsynchronously, without needing a pool thread.
                    root.GetAwaiter().UnsafeOnCompleted(static () => { });
                }

                Pump(root);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            finally
            {
                SetSynchronizationContext(this);
                Stop(failures);
                SetSynchronizationContext(null);
                Complete(root, failures);
            }
        }

        private void Pump(Task root)
        {
            bool preferCallback = false;
            bool preferCancellation = false;

            while (true)
            {
                WorkItem work = default;
                DelayRegistration? timer;
                bool canceled = false;

                lock (_gate)
                {
                    while (true)
                    {
                        if (root.IsCompleted)
                        {
                            return;
                        }

                        timer = null;
                        if (preferCallback && _callbacks.TryDequeue(out work))
                        {
                            preferCallback = false;
                            break;
                        }

                        DelayRegistration? first = _timers.Min;
                        bool due = first != null && first.Deadline <= Stopwatch.GetTimestamp();
                        if (_canceledTimers.Count != 0 && (preferCancellation || !due))
                        {
                            timer = _canceledTimers.Dequeue();
                            canceled = true;
                            preferCancellation = false;
                            preferCallback = true;
                            break;
                        }

                        if (due)
                        {
                            timer = first!;
                            _timers.Remove(timer);
                            preferCancellation = true;
                            preferCallback = true;
                            break;
                        }

                        if (_callbacks.TryDequeue(out work))
                        {
                            preferCallback = false;
                            break;
                        }

                        Monitor.Wait(_gate, GetWaitMilliseconds(first));
                    }
                }

                // Alternate callbacks and timers, and expired and canceled timers.
                // None can starve the others, even with a continuously busy FIFO.
                if (timer != null)
                {
                    timer.Registration.Dispose();
                    if (canceled)
                    {
                        timer.Completion.TrySetCanceled(timer.CancellationToken);
                    }
                    else
                    {
                        timer.Completion.TrySetResult();
                    }
                }
                else
                {
                    Invoke(work.Action);
                }
            }
        }

        private static int GetWaitMilliseconds(DelayRegistration? first)
        {
            if (first == null || first.Deadline == Int128.MaxValue)
            {
                return Timeout.Infinite;
            }

            Int128 remaining = first.Deadline - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                return 0;
            }

            Int128 milliseconds = (remaining * 1000 + Stopwatch.Frequency - 1) / Stopwatch.Frequency;
            return (int)Int128.Min(milliseconds, int.MaxValue);
        }

        private void Stop(List<Exception> failures)
        {
            List<DelayRegistration> timers;
            lock (_gate)
            {
                _stopping = true;
                timers = new List<DelayRegistration>(_timers);
                timers.AddRange(_canceledTimers);
                _timers.Clear();
                _canceledTimers.Clear();
                _root = null;
            }

            foreach (DelayRegistration timer in timers)
            {
                try
                {
                    timer.Registration.Dispose();
                    timer.Completion.TrySetCanceled(timer.CancellationToken.IsCancellationRequested
                        ? timer.CancellationToken
                        : default);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            // Completing channels/timers can queue several layers of captured awaits.
            // Drain those layers before closing Post; pending Enter calls reject instead.
            while (true)
            {
                WorkItem entry;
                lock (_gate)
                {
                    if (!_callbacks.TryDequeue(out entry))
                    {
                        _closed = true;
                        break;
                    }
                }
                try
                {
                    Invoke(entry.Action);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

        private static void Invoke(Action action)
        {
            SynchronizationContext? previous = Current;
            try
            {
                action();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        private void Complete(Task? root, List<Exception> failures)
        {
            if (root?.IsFaulted == true)
            {
                failures.AddRange(root.Exception!.InnerExceptions);
            }

            if (failures.Count != 0)
            {
                _completion.TrySetException(failures);
            }
            else if (root?.IsCanceled == true)
            {
                try
                {
                    root.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException exception)
                {
                    _completion.TrySetCanceled(exception.CancellationToken);
                }
            }
            else
            {
                _completion.TrySetResult();
            }
        }

        private readonly struct WorkItem(Action action, bool isEntry)
        {
            public Action Action { get; } = action;
            public bool IsEntry { get; } = isEntry;
        }

        private sealed class DelayRegistration(
            DedicatedExecution owner, Int128 deadline, ulong sequence, CancellationToken cancellationToken)
        {
            public DedicatedExecution Owner { get; } = owner;
            public Int128 Deadline { get; } = deadline;
            public ulong Sequence { get; } = sequence;
            public CancellationToken CancellationToken { get; } = cancellationToken;

            // These continuations deliberately run on the completing pump, not the
            // pool. Delay is internal; consumer-facing completion is separate.
            public TaskCompletionSource Completion { get; } = new();
            public CancellationTokenRegistration Registration { get; set; }
        }
    }

    internal readonly struct ExecutionAwaitable(DedicatedExecution execution)
    {
        public Awaiter GetAwaiter() => new(execution);

        internal readonly struct Awaiter(DedicatedExecution execution) : ICriticalNotifyCompletion
        {
            public bool IsCompleted => execution.IsCurrent;

            public void GetResult() => execution.GetEnterResult();

            public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);

            public void UnsafeOnCompleted(Action continuation) => execution.PostEntry(continuation);
        }
    }
}