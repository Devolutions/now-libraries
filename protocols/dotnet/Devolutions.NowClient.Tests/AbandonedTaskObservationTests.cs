using System.Runtime.CompilerServices;

namespace Devolutions.NowClient.Tests;

// Each case leaves a worker-side task pending at exit; completing the command writer or disposing the transport
// then faults it. The exact pending task depends on the exit path, so every path is a separate scenario.
public sealed class AbandonedTaskObservationTests
{
    [Theory]
    [InlineData("peer-close", false)]
    [InlineData("peer-close", true)]
    [InlineData("heartbeat-expiry", false)]
    [InlineData("heartbeat-expiry", true)]
    [InlineData("write-fault", false)]
    [InlineData("write-fault", true)]
    public void WorkerExitObservesTheReadsItAbandons(string exit, bool shared)
    {
        TestDriver.Run(() =>
        {
            using var faults = new UnobservedFaults();
            RunAndCollect(() => ExitWorker(exit, shared));
            faults.AssertNone();
        }, timeoutSeconds: 45);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TransportReadFaultingAfterTheExecutionClosedIsObserved(bool shared)
    {
        TestDriver.Run(() =>
        {
            using var faults = new UnobservedFaults();
            var late = new LateFaultingTransport();
            RunAndCollect(() => StopWhileReading(late, shared));
            // The pump is gone; the transport now fails the read it still holds, as a native callback would.
            late.FailPendingRead(new IOException("late native read failure"));
            RunAndCollect(() => { });
            faults.AssertNone();
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ExitWorker(string exit, bool shared)
    {
        var execution = shared ? new NowClientExecution() : null;
        try
        {
            using var peer = new ClientPeer();
            var client = peer.Connect(dedicated: !shared, execution: execution, heartbeat: TimeSpan.FromSeconds(5));
            if (exit == "write-fault")
            {
                TestDriver.Success(client.SessionLock());
                var write = peer.Wire.NextWrite();
                write.Decode<NowMsgSessionLock>();
                write.Fail(new IOException("write failed"));
                TestDriver.Failure(client.Completion);
            }
            else if (exit == "peer-close")
            {
                peer.Wire.Send(NowMsgChannelClose.Graceful());
                TestDriver.Success(client.Completion);
            }
            else
            {
                // Heartbeat expiry is a local stop, not a protocol failure.
                TestDriver.Success(client.Completion, TimeSpan.FromSeconds(25));
            }

            Assert.True(peer.Wire.IsDisposed);
        }
        finally
        {
            if (execution != null)
                TestDriver.Success(execution.DisposeAsync().AsTask());
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StopWhileReading(LateFaultingTransport transport, bool shared)
    {
        var execution = shared ? new NowClientExecution() : null;
        var scheduling = execution != null ? NowClientScheduling.SharedThread(execution) : NowClientScheduling.DedicatedThread;
        var connection = Client.Connect(transport, new NowClientOptions { Scheduling = scheduling });
        transport.CompleteNegotiation();
        var client = TestDriver.Success(connection);
        TestDriver.Until(() => transport.HasPendingRead, "worker read");
        TestDriver.Success(client.StopAsync());
        if (execution != null)
            TestDriver.Success(execution.DisposeAsync().AsTask());
        Assert.True(transport.IsDisposed);
    }

    private static void RunAndCollect(Action action)
    {
        action();
        for (var iteration = 0; iteration < 5; iteration++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private sealed class UnobservedFaults : IDisposable
    {
        private readonly List<string> _faults = new();

        public UnobservedFaults()
        {
            TaskScheduler.UnobservedTaskException += OnUnobserved;
        }

        public void AssertNone()
        {
            lock (_faults)
                Assert.Empty(_faults);
        }

        public void Dispose()
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            args.SetObserved();
            lock (_faults)
                _faults.Add(args.Exception.GetBaseException().ToString());
        }
    }

    // Answers negotiation, then parks the worker's read until told to fail it; Dispose does not touch the read.
    private sealed class LateFaultingTransport : INowTransport
    {
        private readonly TaskCompletionSource<byte[]> _negotiation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<byte[]>? _pending;
        private int _reads;

        public bool HasPendingRead => Volatile.Read(ref _pending) != null;
        public bool IsDisposed { get; private set; }

        public void CompleteNegotiation() =>
            _negotiation.TrySetResult(WireTransport.Encode(ClientPeer.Capabilities()));

        public void FailPendingRead(Exception failure) => Volatile.Read(ref _pending)?.TrySetException(failure);

        public Task Write(byte[] data) => Task.CompletedTask;

        public Task<byte[]> Read(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _reads) == 1)
                return _negotiation.Task;
            var pending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _pending, pending);
            return pending.Task;
        }

        public void Dispose() => IsDisposed = true;
    }
}