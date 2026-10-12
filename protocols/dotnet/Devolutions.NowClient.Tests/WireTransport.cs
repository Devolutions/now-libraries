using System.Collections.Concurrent;
using System.Diagnostics;

namespace Devolutions.NowClient.Tests;

// The peer is driven exclusively by raw threads. No Task.Run, async channel or
// pool timer may complete transport I/O: this models external/native callbacks.
internal sealed class WireTransport : INowTransport
{
    internal sealed record Call(Thread Thread, SynchronizationContext? Context, bool IsBackground, bool IsPoolThread);

    internal abstract class Operation
    {
        private int _yielded;

        protected Operation()
        {
            if (SynchronizationContext.Current is { } context)
                context.Post(_ => Volatile.Write(ref _yielded, 1), null);
            else
                _yielded = 1;
        }

        // A turn of the protocol context proves the caller actually suspended
        // before the native completion; otherwise an already-complete Task can
        // accidentally hide a pool-dependent continuation.
        public bool HasYielded => Volatile.Read(ref _yielded) != 0;
    }

    internal sealed class ReadOperation : Operation
    {
        private readonly TaskCompletionSource<byte[]> _source =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;

        public ReadOperation(CancellationToken token)
        {
            _registration = token.Register(() => _source.TrySetCanceled(token));
        }

        public Task<byte[]> Task => _source.Task;

        public void Complete(byte[] bytes)
        {
            _source.TrySetResult(bytes);
            _registration.Dispose();
        }

        public void Fail(Exception error)
        {
            _source.TrySetException(error);
            _registration.Dispose();
        }
    }

    internal sealed class WriteOperation(byte[] bytes) : Operation
    {
        private readonly TaskCompletionSource _source =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Task => _source.Task;
        public NowMessage.NowMessageView Message => NowMessage.Read(new NowReadCursor(bytes));

        public T Decode<T>() where T : INowDeserialize<T> => Message.Deserialize<T>();
        public void Complete() => _source.TrySetResult();
        public void Fail(Exception error) => _source.TrySetException(error);
    }

    internal sealed class ReadyIo : IDisposable
    {
        private readonly WireTransport _owner;
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly TimeSpan _duration;
        private readonly Action<byte[]> _onWrite;
        private Call? _call;
        private int _stopped;
        private long _reads;
        private long _writes;

        internal ReadyIo(WireTransport owner, TimeSpan duration, byte[]? repeatedRead, Action<byte[]> onWrite)
        {
            _owner = owner;
            _duration = duration;
            ReadFrame = repeatedRead?.ToArray();
            _onWrite = onWrite;
        }

        internal byte[]? ReadFrame { get; }
        internal bool IsActive => Volatile.Read(ref _stopped) == 0 && _elapsed.Elapsed < _duration;
        public long Reads => Interlocked.Read(ref _reads);
        public long Writes => Interlocked.Read(ref _writes);

        // Keep only one call record, not millions of completed tasks and buffers.
        // Still validate affinity on every synchronous operation.
        private void RecordCall()
        {
            var thread = Thread.CurrentThread;
            var context = SynchronizationContext.Current;
            var call = Volatile.Read(ref _call);
            if (call == null)
            {
                var candidate = new Call(thread, context, thread.IsBackground, thread.IsThreadPoolThread);
                call = Interlocked.CompareExchange(ref _call, candidate, null);
                if (call == null)
                {
                    _owner.Calls.Enqueue(candidate);
                    return;
                }
            }
            Assert.Same(call.Thread, thread);
            Assert.Same(call.Context, context);
        }

        internal Task<byte[]> Read(CancellationToken cancellationToken)
        {
            RecordCall();
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _reads);
            return Task.FromResult(ReadFrame!);
        }

        internal Task Write(byte[] bytes)
        {
            RecordCall();
            Interlocked.Increment(ref _writes);
            _onWrite(bytes);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _stopped, 1);
            Interlocked.CompareExchange(ref _owner._readyIo, null, this);
        }
    }

    private readonly BlockingCollection<ReadOperation> _reads = new();
    private readonly BlockingCollection<WriteOperation> _writes = new();
    private readonly ConcurrentBag<ReadOperation> _allReads = new();
    private readonly ConcurrentBag<WriteOperation> _allWrites = new();
    private ReadyIo? _readyIo;
    private int _disposed;
    private int _disposeCalls;

    public ConcurrentQueue<Call> Calls { get; } = new();
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public int DisposeCalls => Volatile.Read(ref _disposeCalls);
    public bool AllOperationsCompleted => _allReads.All(r => r.Task.IsCompleted) &&
                                          _allWrites.All(w => w.Task.IsCompleted);

    public ReadyIo BeginReadyIo(TimeSpan duration, Action<byte[]> onWrite, byte[]? repeatedRead = null)
    {
        ArgumentNullException.ThrowIfNull(onWrite);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (repeatedRead is { Length: 0 } || repeatedRead is { Length: > 65536 })
            throw new ArgumentOutOfRangeException(nameof(repeatedRead));
        var ready = new ReadyIo(this, duration, repeatedRead, onWrite);
        if (Interlocked.CompareExchange(ref _readyIo, ready, null) != null)
            throw new InvalidOperationException("Synchronous I/O is already enabled.");
        return ready;
    }

    public Task<byte[]> Read(CancellationToken cancellationToken = default)
    {
        var ready = Volatile.Read(ref _readyIo);
        if (!IsDisposed && ready is { IsActive: true, ReadFrame: not null })
            return ready.Read(cancellationToken);
        RecordCall();
        var read = new ReadOperation(cancellationToken);
        _allReads.Add(read);
        if (IsDisposed)
            read.Fail(new ObjectDisposedException(nameof(WireTransport)));
        else
            _reads.Add(read);
        return read.Task;
    }

    public Task Write(byte[] data)
    {
        var ready = Volatile.Read(ref _readyIo);
        if (!IsDisposed && ready is { IsActive: true })
            return ready.Write(data);
        RecordCall();
        var write = new WriteOperation(data.ToArray());
        _allWrites.Add(write);
        if (IsDisposed)
            write.Fail(new ObjectDisposedException(nameof(WireTransport)));
        else
            _writes.Add(write);
        return write.Task;
    }

    public ReadOperation NextRead()
    {
        ReadOperation? result = null;
        TestDriver.Until(() =>
        {
            while (_reads.TryTake(out var read))
            {
                if (!read.Task.IsCompleted)
                {
                    result = read;
                    return true;
                }
            }
            return false;
        }, "transport Read");
        TestDriver.Until(() => result!.HasYielded, "protocol suspension on Read");
        return result!;
    }

    public void WaitForPendingRead()
    {
        TestDriver.Until(() => _reads.Any(read => !read.Task.IsCompleted && read.HasYielded),
            "worker awaiting external read and commands");
    }

    public WriteOperation NextWrite()
    {
        TestDriver.Until(() => _writes.Count > 0, "transport Write");
        Assert.True(_writes.TryTake(out var write));
        TestDriver.Until(() => write.HasYielded, "protocol suspension on Write");
        return write;
    }

    public T Receive<T>() where T : INowDeserialize<T>
    {
        var write = NextWrite();
        var message = write.Decode<T>();
        write.Complete();
        return message;
    }

    public void Send(INowSerialize message) => NextRead().Complete(Encode(message));

    public void SendFragmented(INowSerialize message)
    {
        var bytes = Encode(message);
        foreach (var value in bytes)
            NextRead().Complete([value]);
    }

    public static byte[] Encode(INowSerialize message)
    {
        var bytes = new byte[message.Size];
        message.Serialize(new NowWriteCursor(bytes));
        return bytes;
    }

    private void RecordCall()
    {
        var thread = Thread.CurrentThread;
        Calls.Enqueue(new(thread, SynchronizationContext.Current, thread.IsBackground, thread.IsThreadPoolThread));
    }

    public Thread AssertDedicatedThread()
    {
        var calls = Calls.ToArray();
        Assert.NotEmpty(calls);
        var thread = calls[0].Thread;
        Assert.True(calls[0].IsBackground);
        Assert.False(calls[0].IsPoolThread);
        Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, thread.ManagedThreadId);
        Assert.NotNull(calls[0].Context);
        Assert.All(calls, call =>
        {
            Assert.Same(thread, call.Thread);
            Assert.Same(calls[0].Context, call.Context);
        });
        return thread;
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _disposeCalls);
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        foreach (var read in _allReads)
            read.Fail(new ObjectDisposedException(nameof(WireTransport)));
        foreach (var write in _allWrites)
            write.Fail(new ObjectDisposedException(nameof(WireTransport)));
    }
}