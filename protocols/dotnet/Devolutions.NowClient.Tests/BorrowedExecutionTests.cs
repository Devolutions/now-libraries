namespace Devolutions.NowClient.Tests;

public sealed class BorrowedExecutionTests
{
    [Theory]
    [InlineData("stop")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("close")]
    [InlineData("fault")]
    public void ClientCleanupDoesNotDisposeBorrowedExecutorOrOtherClients(string cleanup)
    {
        TestDriver.Run(() =>
        {
            var execution = new NowClientExecution();
            Thread? worker = null;
            try
            {
                using var firstPeer = new ClientPeer();
                using var secondPeer = new ClientPeer();
                var first = firstPeer.Connect(execution: execution);
                var second = secondPeer.Connect(execution: execution);
                worker = firstPeer.Wire.AssertDedicatedThread();
                Assert.Same(worker, secondPeer.Wire.AssertDedicatedThread());
                Assert.NotSame(execution.Completion, first.Completion);
                Assert.NotSame(first.Completion, second.Completion);
                var pending = first.RdmSync();
                firstPeer.Wire.Receive<NowMsgRdmCapabilities>();
                var fault = new IOException("borrowed worker read failed");
                switch (cleanup)
                {
                    case "stop": TestDriver.Success(first.StopAsync()); break;
                    case "cancel": firstPeer.Cancel(); break;
                    case "dispose": TestDriver.Success(first.DisposeAsync().AsTask()); break;
                    case "close": firstPeer.Wire.Send(NowMsgChannelClose.Graceful()); break;
                    case "fault": firstPeer.Wire.NextRead().Fail(fault); break;
                }
                TestDriver.Failure(pending);
                if (cleanup == "fault")
                    Assert.Same(fault, TestDriver.Failure(first.Completion));
                firstPeer.AssertStopped(successful: cleanup != "fault");
                Assert.False(execution.Completion.IsCompleted);
                Assert.True(worker.IsAlive);
                Assert.False(second.Completion.IsCompleted);
                Assert.Same(worker, TestDriver.Success(execution.Run(() => Task.FromResult(Thread.CurrentThread))));
                TestDriver.Success(second.SessionLock());
                secondPeer.Wire.Receive<NowMsgSessionLock>();
                var sync = second.RdmSync();
                secondPeer.Wire.Receive<NowMsgRdmCapabilities>();
                secondPeer.Wire.SendFragmented(ClientPeer.RdmCapabilities());
                TestDriver.Success(sync);
                Assert.True(TestDriver.Success(second.IsRdmAppAvailable()));
                TestDriver.Success(second.DisposeAsync().AsTask());
                secondPeer.AssertStopped();
                TestDriver.Success(execution.Delay(TimeSpan.FromMilliseconds(10)));
                Assert.False(execution.Completion.IsCompleted);
            }
            finally
            {
                TestDriver.Success(execution.DisposeAsync().AsTask());
            }
            TestDriver.Success(execution.Completion);
            TestDriver.ThreadExited(worker!);
        });
    }

    [Theory]
    [InlineData("write")]
    [InlineData("read")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    public void ExecutorCanBeReusedAfterFailedClientNegotiation(string failure)
    {
        TestDriver.Run(() =>
        {
            var execution = new NowClientExecution();
            Thread? worker = null;
            try
            {
                using (var failedPeer = new ClientPeer())
                {
                    var connection = failedPeer.BeginConnect(execution: execution,
                        timeout: failure == "timeout" ? TimeSpan.FromMilliseconds(200) : null);
                    var offer = failedPeer.Wire.NextWrite();
                    offer.Decode<NowMsgChannelCapset>();
                    var fault = new IOException($"negotiation {failure}");
                    if (failure == "write")
                        offer.Fail(fault);
                    else if (failure == "cancel")
                        failedPeer.Cancel();
                    else
                    {
                        offer.Complete();
                        var read = failedPeer.Wire.NextRead();
                        if (failure == "read")
                            read.Fail(fault);
                    }
                    var error = TestDriver.Failure(connection);
                    if (failure == "timeout")
                        Assert.IsType<TimeoutException>(error);
                    else if (failure == "cancel")
                        Assert.IsAssignableFrom<OperationCanceledException>(error);
                    else
                        Assert.Same(fault, error);
                    Assert.True(failedPeer.Wire.IsDisposed);
                    Assert.Equal(1, failedPeer.Wire.DisposeCalls);
                    Assert.True(failedPeer.Wire.AllOperationsCompleted);
                    worker = failedPeer.Wire.AssertDedicatedThread();
                }
                Assert.False(execution.Completion.IsCompleted);
                Assert.True(worker.IsAlive);
                using var nextPeer = new ClientPeer();
                var next = nextPeer.Connect(execution: execution);
                Assert.Same(worker, nextPeer.Wire.AssertDedicatedThread());
                var start = next.RdmStart();
                nextPeer.Wire.Receive<NowMsgRdmCapabilities>();
                nextPeer.Wire.Send(ClientPeer.RdmCapabilities());
                TestDriver.Success(start);
                nextPeer.Wire.Receive<NowMsgRdmAppStart>();
                TestDriver.Success(next.StopAsync());
                nextPeer.AssertStopped();
                Assert.False(execution.Completion.IsCompleted);
            }
            finally
            {
                TestDriver.Success(execution.DisposeAsync().AsTask());
            }
            TestDriver.Success(execution.Completion);
            TestDriver.ThreadExited(worker!);
        });
    }

    [Fact]
    public void BorrowedExecutionAndOwnedThreadOptionsAreMutuallyExclusive()
    {
        TestDriver.Run(() =>
        {
            var execution = new NowClientExecution();
            using var wire = new WireTransport();
            try
            {
                var error = Assert.Throws<ArgumentException>(() =>
                {
                    _ = Client.Connect(wire, new NowClientOptions
                    {
                        Execution = execution,
                        UseDedicatedThread = true,
                    });
                });
                Assert.Equal("options", error.ParamName);
                Assert.Empty(wire.Calls);
                Assert.False(wire.IsDisposed);
                TestDriver.Success(execution.Run(() => Task.CompletedTask));
            }
            finally
            {
                TestDriver.Success(execution.DisposeAsync().AsTask());
            }
        });
    }

    [Fact]
    public void DisposedExecutorRejectsClientConnectionAndCleansTransport()
    {
        TestDriver.Run(() =>
        {
            var execution = new NowClientExecution();
            TestDriver.Success(execution.DisposeAsync().AsTask());
            var wire = new WireTransport();
            try
            {
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    _ = Client.Connect(wire, new NowClientOptions { Execution = execution });
                });
                Assert.Empty(wire.Calls);
                Assert.True(wire.IsDisposed);
                Assert.Equal(1, wire.DisposeCalls);
                Assert.True(execution.Completion.IsCompletedSuccessfully);
            }
            finally
            {
                if (!wire.IsDisposed)
                    wire.Dispose();
            }
        });
    }
}