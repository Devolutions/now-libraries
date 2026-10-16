using System.Diagnostics;

namespace Devolutions.NowClient.Tests;

public sealed class ThreadPoolStarvationTests
{
    [Fact]
    public void DedicatedClientNegotiatesDispatchesReceivesAndTimesOutWithEntirePoolBlocked()
    {
        // The xUnit thread only joins. The raw driver owns assertions, deadlines,
        // transport completions and, critically, restoration in failure paths.
        TestDriver.Run(() =>
        {
            using var starvation = new PoolStarvation();
            try
            {
                starvation.Hold();
                ExerciseOuterConnectionsWhileStarved(starvation);
                ExerciseClientWhileStarved(starvation);
            }
            finally
            {
                starvation.Release();
            }
            starvation.AssertRestored();
        }, timeoutSeconds: 90);
    }

    private static void ExerciseOuterConnectionsWhileStarved(PoolStarvation starvation)
    {
        var execution = new NowClientExecution();
        Thread? worker = null;
        try
        {
            foreach (var start in new[] { false, true })
            {
                using var peer = new ClientPeer();
                var readoption = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var waitingForReadoption = 0;
                var callbacks = 0;
                Thread? callbackThread = null;
                Task<Client>? connection = null;
                var ui = new FrozenUiContext();
                var outer = ui.Invoke(() => execution.Run(async () =>
                {
                    worker = Thread.CurrentThread;
                    var client = await (connection = peer.BeginConnect(execution: execution));
                    await client.SetRdmAppNotifyHandler((_, _, _) =>
                    {
                        callbackThread = Thread.CurrentThread;
                        Interlocked.Increment(ref callbacks);
                    });
                    await client.SetRdmSessionNotifyHandler((_, _, _) =>
                    {
                        callbackThread = Thread.CurrentThread;
                        Interlocked.Increment(ref callbacks);
                    });
                    if (start)
                        await client.RdmStart();
                    else
                        await client.RdmSync();
                    await execution.Delay(TimeSpan.FromMilliseconds(40));
                    Volatile.Write(ref waitingForReadoption, 1);
                    var adopted = await readoption.Task;
                    Assert.Same(worker, Thread.CurrentThread);
                    await execution.Delay(TimeSpan.FromMilliseconds(10));
                    return adopted;
                }));
                try
                {
                    peer.Wire.Receive<NowMsgChannelCapset>();
                    peer.Wire.SendFragmented(ClientPeer.Capabilities());
                    TestDriver.Until(() => Volatile.Read(ref connection) != null, "outer connection admission");
                    var client = peer.FinishConnect(connection!);
                    peer.Wire.Receive<NowMsgRdmCapabilities>();
                    peer.Wire.SendFragmented(ClientPeer.RdmCapabilities());
                    if (start)
                        Assert.Equal(45u, peer.Wire.Receive<NowMsgRdmAppStart>().Timeout);
                    TestDriver.Until(() => Volatile.Read(ref waitingForReadoption) == 1,
                        "outer operation past RDM and dedicated delay");
                    TestDriver.Success(execution.Run(() => Task.CompletedTask));
                    Assert.False(outer.IsCompleted);
                    Assert.False(readoption.Task.IsCompleted);
                    starvation.AssertHeld();
                    // The callback only records a notification. The outer operation,
                    // not the protocol callback, awaits asynchronous re-adoption.
                    peer.Wire.NextRead().Complete(
                    [
                        .. WireTransport.Encode(new NowMsgRdmAppNotify(
                            NowRdmAppState.Ready, NowRdmReason.NotSpecified, "outer-chain")),
                        .. WireTransport.Encode(NowMsgRdmSessionNotify.Close(Guid.NewGuid(), "outer-chain")),
                    ]);
                    TestDriver.Until(() => Volatile.Read(ref callbacks) == 2,
                        "registered callbacks while outer operation is awaiting re-adoption");
                    Assert.Same(worker, callbackThread);
                    readoption.SetResult(true);
                    Assert.True(TestDriver.Success(outer));
                    Assert.Equal(0, ui.Posts);
                    Assert.Same(worker, peer.Wire.AssertDedicatedThread());
                    Assert.False(client.Completion.IsCompleted);
                    Assert.False(execution.Completion.IsCompleted);
                    // Returning from the outer Run must not stop its borrowed worker.
                    TestDriver.Success(client.SessionLock());
                    peer.Wire.Receive<NowMsgSessionLock>();
                    TestDriver.Success(client.StopAsync());
                    peer.AssertStopped();
                    starvation.AssertHeld();
                }
                finally
                {
                    readoption.TrySetResult(true);
                }
            }
            using var cancel = new CancellationTokenSource();
            var infinite = execution.Delay(Timeout.InfiniteTimeSpan, cancel.Token);
            TestDriver.Success(execution.Run(() => Task.CompletedTask));
            Assert.False(infinite.IsCompleted);
            cancel.Cancel();
            Assert.Equal(cancel.Token,
                Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(infinite)).CancellationToken);
        }
        finally
        {
            // All borrowed clients have stopped before the owner is disposed.
            TestDriver.Success(execution.DisposeAsync().AsTask());
        }
        TestDriver.Success(execution.Completion);
        TestDriver.ThreadExited(worker!);
        starvation.AssertHeld();
    }

    private static void ExerciseClientWhileStarved(PoolStarvation starvation)
    {
        using var heartbeatPeer = new ClientPeer();
        using var requestPeer = new ClientPeer();
        var heartbeatClient = heartbeatPeer.Connect(heartbeat: TimeSpan.FromSeconds(5));
        var requestClient = requestPeer.Connect();
        starvation.AssertHeld();

        // The worker is already waiting for a command and a native transport
        // read. Completing either must not need Task.WhenAny's pool dispatch.
        TestDriver.Success(heartbeatClient.SessionLock());
        heartbeatPeer.Wire.Receive<NowMsgSessionLock>();
        var ui = new FrozenUiContext();
        var start = ui.Invoke(() => heartbeatClient.RdmStart());
        heartbeatPeer.Wire.Receive<NowMsgRdmCapabilities>();
        heartbeatPeer.Wire.SendFragmented(ClientPeer.RdmCapabilities());
        TestDriver.Success(start);
        Assert.Equal(45u, heartbeatPeer.Wire.Receive<NowMsgRdmAppStart>().Timeout);
        Assert.Equal(0, ui.Posts);
        starvation.AssertHeld();

        var sync = ui.Invoke(requestClient.RdmSync);
        requestPeer.Wire.Receive<NowMsgRdmCapabilities>();
        requestPeer.Wire.Send(ClientPeer.RdmCapabilities());
        TestDriver.Success(sync);
        var version = TestDriver.Success(requestClient.GetRdmVersion());
        Assert.Equal("2026.3.1.0", version!.Version);

        Thread? notificationThread = null;
        var notificationCount = 0;
        TestDriver.Success(heartbeatClient.SetRdmAppNotifyHandler((_, _, _) =>
        {
            notificationThread = Thread.CurrentThread;
            Interlocked.Increment(ref notificationCount);
        }));
        TestDriver.Success(heartbeatClient.SessionLock());
        heartbeatPeer.Wire.Receive<NowMsgSessionLock>();
        var heartbeatRead = heartbeatPeer.Wire.NextRead();
        var heartbeatElapsed = Stopwatch.StartNew();
        heartbeatRead.Complete(
        [
            .. WireTransport.Encode(new NowMsgChannelHeartbeat()),
            .. WireTransport.Encode(new NowMsgRdmAppNotify(NowRdmAppState.Ready, NowRdmReason.NotSpecified, "starved")),
        ]);
        TestDriver.Until(() => Volatile.Read(ref notificationCount) == 1, "notification during pool starvation");
        Assert.Same(heartbeatPeer.Wire.AssertDedicatedThread(), notificationThread);
        starvation.AssertHeld();

        var requestElapsed = Stopwatch.StartNew();
        var timeoutRequest = ui.Invoke(requestClient.RdmSync);
        requestPeer.Wire.Receive<NowMsgRdmCapabilities>();
        Assert.False(timeoutRequest.IsCompleted);

        // Verify negotiation timers and cancellation as well as response timers
        // without ever scheduling the peer or a test timeout on the pool.
        using (var timeoutPeer = new ClientPeer())
        {
            var connection = timeoutPeer.BeginConnect(timeout: TimeSpan.FromMilliseconds(250));
            timeoutPeer.Wire.Receive<NowMsgChannelCapset>();
            timeoutPeer.Wire.NextRead();
            Assert.IsType<TimeoutException>(TestDriver.Failure(connection));
        }
        using (var cancelledPeer = new ClientPeer())
        {
            var connection = cancelledPeer.BeginConnect();
            cancelledPeer.Wire.NextWrite().Decode<NowMsgChannelCapset>();
            cancelledPeer.Cancel();
            Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(connection));
        }
        starvation.AssertHeld();

        TestDriver.Until(() =>
        {
            starvation.AssertHeld();
            return timeoutRequest.IsCompleted;
        }, "RDM's 10-second request deadline while starved",
            TimeSpan.FromSeconds(15));
        Assert.True(requestElapsed.Elapsed >= TimeSpan.FromSeconds(9));
        Assert.True(requestElapsed.Elapsed < TimeSpan.FromSeconds(16));
        Assert.IsType<TimeoutException>(TestDriver.Failure(timeoutRequest));
        Assert.Equal(0, ui.Posts);
        Assert.False(requestClient.Completion.IsCompleted);

        // Heartbeats have a 5s minimum plus 5s leeway. The worker checks at
        // twice the negotiated interval, so a just-received heartbeat can
        // require a second check before it is considered expired.
        TestDriver.Until(() =>
        {
            starvation.AssertHeld();
            return heartbeatClient.Completion.IsCompleted;
        }, "heartbeat expiry while starved", TimeSpan.FromSeconds(15));
        Assert.True(heartbeatElapsed.Elapsed >= TimeSpan.FromSeconds(9));
        Assert.True(heartbeatElapsed.Elapsed < TimeSpan.FromSeconds(26));
        heartbeatPeer.AssertStopped();
        starvation.AssertHeld();

        // Prove that the surviving protocol pump still works after timeout,
        // then complete graceful and immediate shutdown without freeing pool.
        TestDriver.Success(requestClient.SessionLock());
        requestPeer.Wire.Receive<NowMsgSessionLock>();
        TestDriver.Success(requestClient.ForceTerminate());
        Assert.True(requestPeer.Wire.Receive<NowMsgChannelClose>().IsGraceful);
        TestDriver.Success(requestClient.Completion);
        requestPeer.AssertStopped();

        using var stoppedPeer = new ClientPeer();
        var stoppedClient = stoppedPeer.Connect();
        var pending = stoppedClient.RdmSync();
        stoppedPeer.Wire.NextWrite().Decode<NowMsgRdmCapabilities>();
        TestDriver.Success(stoppedClient.StopAsync());
        Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(pending));
        TestDriver.Success(stoppedClient.DisposeAsync().AsTask());
        stoppedPeer.AssertStopped();
        starvation.AssertHeld();
    }

    private sealed class PoolStarvation : IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private readonly List<Task> _blockers = [];
        private readonly int _oldMinWorker;
        private readonly int _oldMinIo;
        private readonly int _oldMaxWorker;
        private readonly int _oldMaxIo;
        private Task? _marker;
        private int _entered;
        private int _escaped;
        private bool _released;

        public PoolStarvation()
        {
            ThreadPool.GetMinThreads(out _oldMinWorker, out _oldMinIo);
            ThreadPool.GetMaxThreads(out _oldMaxWorker, out _oldMaxIo);
        }

        public void Hold()
        {
            var cap = Math.Min(_oldMaxWorker, Math.Max(4, Environment.ProcessorCount));
            Assert.True(ThreadPool.SetMinThreads(1, _oldMinIo));
            Assert.True(ThreadPool.SetMaxThreads(cap, _oldMaxIo));
            Assert.True(ThreadPool.SetMinThreads(cap, _oldMinIo));
            for (var i = 0; i < cap; i++)
            {
                _blockers.Add(Task.Run(() =>
                {
                    Interlocked.Increment(ref _entered);
                    if (!_release.Wait(TimeSpan.FromSeconds(80)))
                        Interlocked.Increment(ref _escaped);
                }));
            }

            var stable = Stopwatch.StartNew();
            var previousEntered = -1;
            TestDriver.Until(() =>
            {
                ThreadPool.GetAvailableThreads(out var available, out _);
                var entered = Volatile.Read(ref _entered);
                if (available != 0 || entered == 0 || entered != previousEntered)
                {
                    previousEntered = entered;
                    stable.Restart();
                    return false;
                }
                return stable.Elapsed >= TimeSpan.FromMilliseconds(200);
            }, "all capped pool workers blocked", TimeSpan.FromSeconds(15));

            _marker = Task.Run(() => { });
            AssertHeld();
        }

        public void AssertHeld()
        {
            Assert.NotNull(_marker);
            Assert.False(_marker.IsCompleted, "The pool marker ran; starvation proof is invalid.");
            Assert.True(Volatile.Read(ref _entered) > 0);
            Assert.Equal(0, Volatile.Read(ref _escaped));
            ThreadPool.GetAvailableThreads(out var available, out _);
            Assert.Equal(0, available);
        }

        public void Release()
        {
            if (_released)
                return;
            _released = true;
            _release.Set();
            // Lower min first if the test temporarily raised it above the
            // original max. Restore both worker and I/O settings in all paths.
            var lowered = ThreadPool.SetMinThreads(1, _oldMinIo);
            var restoredMax = ThreadPool.SetMaxThreads(_oldMaxWorker, _oldMaxIo);
            var restoredMin = ThreadPool.SetMinThreads(_oldMinWorker, _oldMinIo);
            Assert.True(lowered && restoredMax && restoredMin, "Could not restore process ThreadPool settings.");
            foreach (var blocker in _blockers)
                TestDriver.Success(blocker);
            if (_marker != null)
                TestDriver.Success(_marker);
        }

        public void AssertRestored()
        {
            ThreadPool.GetMinThreads(out var minWorker, out var minIo);
            ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);
            Assert.Equal((_oldMinWorker, _oldMinIo), (minWorker, minIo));
            Assert.Equal((_oldMaxWorker, _oldMaxIo), (maxWorker, maxIo));
            Assert.All(_blockers, blocker => Assert.True(blocker.IsCompletedSuccessfully));
            Assert.True(_marker!.IsCompletedSuccessfully);
        }

        public void Dispose()
        {
            try
            {
                Release();
            }
            finally
            {
                _release.Dispose();
            }
        }
    }
}