using System.Collections.Concurrent;
using System.Diagnostics;

namespace Devolutions.NowClient.Tests;

public sealed class ClientIntegrationTests
{
    [Fact]
    public void OptionsPreserveDefaultDispatchAndTenSecondConnectTimeout()
    {
        var options = new NowClientOptions();
        Assert.Same(NowClientScheduling.ThreadPool, options.Scheduling);
        Assert.Equal(TimeSpan.FromSeconds(10), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), options.RequestedHeartbeatInterval);
        Assert.Equal(NowClientHeartbeatPolicy.FollowPeer, options.UnspecifiedPeerHeartbeat);
    }

    [Fact]
    public void NegotiationAndCommandsResumeOnOneBackgroundProtocolThread()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var connection = peer.BeginConnect();
            var offer = peer.Wire.NextWrite();
            Assert.Equal(NowCapabilitySession.All, offer.Decode<NowMsgChannelCapset>().SessionCapset);
            Assert.False(connection.IsCompleted);
            offer.Complete();

            peer.Wire.SendFragmented(ClientPeer.Capabilities());
            var client = peer.FinishConnect(connection);
            var read = peer.Wire.NextRead();
            Assert.False(read.Task.IsCompleted);
            TestDriver.Success(client.SessionLock());
            peer.Wire.Receive<NowMsgSessionLock>();
            Assert.False(read.Task.IsCompleted);

            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultAndExplicitPoolDispatchRemainCompatible(bool legacyOverload)
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect(dedicated: false, legacyOverload: legacyOverload);
            TestDriver.Success(client.SessionLock());
            peer.Wire.Receive<NowMsgSessionLock>();
            var sync = client.RdmSync();
            peer.Wire.Receive<NowMsgRdmCapabilities>();
            peer.Wire.Send(ClientPeer.RdmCapabilities());
            TestDriver.Success(sync);
            Assert.True(TestDriver.Success(client.IsRdmAppAvailable()));
            Assert.Contains(peer.Wire.Calls, call => call.IsPoolThread);
            Assert.All(peer.Wire.Calls, call => Assert.Null(call.Context));
            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RdmSyncAndRdmStartDoNotCaptureForeignUiContext(bool startPerformsSync)
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect();
            var ui = new FrozenUiContext();
            var parameters = new RdmStartParams()
                .LaunchFlags(NowRdmLaunchFlags.JumpMode | NowRdmLaunchFlags.Maximized)
                .Timeout(TimeSpan.FromSeconds(37));
            var operation = ui.Invoke(() => startPerformsSync ? client.RdmStart(parameters) : client.RdmSync());
            var request = peer.Wire.Receive<NowMsgRdmCapabilities>();
            Assert.Equal("", request.RdmVersion);
            Assert.False(operation.IsCompleted);
            peer.Wire.SendFragmented(ClientPeer.RdmCapabilities());
            TestDriver.Success(operation);
            if (!startPerformsSync)
                TestDriver.Success(ui.Invoke(() => client.RdmStart(parameters)));

            var start = peer.Wire.Receive<NowMsgRdmAppStart>();
            Assert.True(start.IsJumpMode);
            Assert.True(start.IsMaximized);
            Assert.Equal(37u, start.Timeout);
            Assert.True(TestDriver.Success(client.IsRdmAppAvailable()));
            var version = TestDriver.Success(client.GetRdmVersion());
            Assert.NotNull(version);
            Assert.Equal("2026.3.1.0", version.Version);
            Assert.Equal("integration", version.Extra);
            Assert.Equal(0, ui.Posts);
            peer.Wire.AssertDedicatedThread();
        });
    }

    [Fact]
    public void CoalescedResponsesAndNotificationsExecuteNonblockingOnProtocolThread()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect();
            var callbacks = new ConcurrentQueue<(Thread Thread, string Data)>();
            var sessionId = Guid.NewGuid();
            TestDriver.Success(client.SetRdmAppNotifyHandler((state, reason, data) =>
                callbacks.Enqueue((Thread.CurrentThread, $"{state}/{reason}/{data}"))));
            TestDriver.Success(client.SetRdmSessionNotifyHandler((id, kind, data) =>
                callbacks.Enqueue((Thread.CurrentThread, $"{id}/{kind}/{data}"))));

            var handler = TestDriver.Success(client.SessionMessageBox(new MessageBoxParams("Wire message").Title("Test")));
            var request = peer.Wire.Receive<NowMsgSessionMessageBoxReq>();
            Assert.Equal(handler.RequestId, request.RequestId);
            var response = handler.GetResponse();
            Assert.False(response.IsCompleted);

            var frames = new[]
            {
                WireTransport.Encode(new NowMsgRdmAppNotify(NowRdmAppState.Ready, NowRdmReason.NotSpecified, "ready")),
                WireTransport.Encode(NowMsgSessionMessageBoxRsp.Success(request.RequestId, NowMsgSessionMessageBoxRsp.MessageBoxResponse.Yes)),
                WireTransport.Encode(NowMsgRdmSessionNotify.Close(sessionId, "closed")),
            };
            peer.Wire.NextRead().Complete(frames.SelectMany(frame => frame).ToArray());
            Assert.Equal(NowMsgSessionMessageBoxRsp.MessageBoxResponse.Yes, TestDriver.Success(response));
            TestDriver.Until(() => callbacks.Count == 2, "both notification callbacks");
            var thread = peer.Wire.AssertDedicatedThread();
            Assert.All(callbacks, callback => Assert.Same(thread, callback.Thread));
            Assert.Equal("Ready/NotSpecified/ready", callbacks.First().Data);
            Assert.Contains(sessionId.ToString(), callbacks.Last().Data);
            Assert.EndsWith("/closed", callbacks.Last().Data);

            TestDriver.Success(client.SessionLogoff());
            peer.Wire.Receive<NowMsgSessionLogoff>();
        });
    }

    [Theory]
    [InlineData("close")]
    [InlineData("eof")]
    [InlineData("read")]
    [InlineData("write")]
    public void RemoteTerminationCompletesWorkerAndPendingRequest(string cause)
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect();
            var request = client.RdmSync();
            var write = peer.Wire.NextWrite();
            write.Decode<NowMsgRdmCapabilities>();
            var fault = new IOException($"Injected {cause} failure");
            if (cause == "write")
            {
                write.Fail(fault);
            }
            else
            {
                write.Complete();
                var read = peer.Wire.NextRead();
                switch (cause)
                {
                    case "close": read.Complete(WireTransport.Encode(NowMsgChannelClose.Graceful())); break;
                    case "eof": read.Complete([]); break;
                    case "read": read.Fail(fault); break;
                }
            }

            TestDriver.Failure(request);
            if (cause == "close")
                TestDriver.Success(client.Completion);
            else if (cause == "eof")
                Assert.IsAssignableFrom<IOException>(TestDriver.Failure(client.Completion));
            else
                Assert.Same(fault, TestDriver.Failure(client.Completion));
            peer.AssertStopped(successful: cause == "close");
            TestDriver.Failure(client.SessionLock());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NegotiationReadOrWriteFailureIsObservableAndDisposesTransport(bool duringWrite)
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var connection = peer.BeginConnect();
            var write = peer.Wire.NextWrite();
            var fault = new IOException("Negotiation I/O failed");
            if (duringWrite)
                write.Fail(fault);
            else
            {
                write.Complete();
                peer.Wire.NextRead().Fail(fault);
            }
            Assert.Same(fault, TestDriver.Failure(connection));
            Assert.True(peer.Wire.IsDisposed);
            Assert.Equal(1, peer.Wire.DisposeCalls);
            TestDriver.ThreadExited(peer.Wire.AssertDedicatedThread());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationInterruptsNegotiationReadAndUncancellableWrite(bool duringWrite)
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var connection = peer.BeginConnect();
            var write = peer.Wire.NextWrite();
            if (!duringWrite)
            {
                write.Complete();
                peer.Wire.NextRead();
            }
            peer.Cancel();
            Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(connection));
            Assert.True(peer.Wire.IsDisposed);
            Assert.True(peer.Wire.AllOperationsCompleted);
            Assert.Equal(1, peer.Wire.DisposeCalls);
            TestDriver.ThreadExited(peer.Wire.AssertDedicatedThread());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConnectTimeoutCoversBothNegotiationReadAndWrite(bool duringWrite)
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var elapsed = Stopwatch.StartNew();
            var connection = peer.BeginConnect(timeout: TimeSpan.FromMilliseconds(200));
            var write = peer.Wire.NextWrite();
            if (!duringWrite)
            {
                write.Complete();
                peer.Wire.NextRead();
            }
            Assert.IsType<TimeoutException>(TestDriver.Failure(connection));
            Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(150));
            Assert.True(peer.Wire.IsDisposed);
            Assert.True(peer.Wire.AllOperationsCompleted);
            TestDriver.ThreadExited(peer.Wire.AssertDedicatedThread());
        });
    }

    [Theory]
    [InlineData("stop", false)]
    [InlineData("stop", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    [InlineData("dispose", false)]
    [InlineData("dispose", true)]
    public void ExplicitShutdownSucceedsAndReleasesPendingReadWriteAndRdmRequest(string method, bool duringWrite)
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect();
            var request = client.RdmSync();
            var write = peer.Wire.NextWrite();
            write.Decode<NowMsgRdmCapabilities>();
            if (!duringWrite)
            {
                write.Complete();
                peer.Wire.NextRead();
            }

            switch (method)
            {
                case "stop":
                    Assert.Same(client.Completion, client.StopAsync());
                    break;
                case "cancel":
                    peer.Cancel();
                    break;
                case "dispose":
                    TestDriver.Success(((IAsyncDisposable)client).DisposeAsync().AsTask());
                    break;
            }

            TestDriver.Success(client.Completion);
            Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(request));
            TestDriver.Success(client.StopAsync());
            TestDriver.Success(client.DisposeAsync().AsTask());
            peer.AssertStopped();
        });
    }

    [Fact]
    public void ForceTerminateRemainsQueuedGracefulAndWaitsForCloseWrite()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect();
            var pending = client.RdmSync();
            peer.Wire.Receive<NowMsgRdmCapabilities>();
            TestDriver.Success(client.SessionLock());
            var first = peer.Wire.NextWrite();
            first.Decode<NowMsgSessionLock>();
            var terminate = client.ForceTerminate();
            Assert.False(client.Completion.IsCompleted);
            first.Complete();

            var close = peer.Wire.NextWrite();
            Assert.True(close.Decode<NowMsgChannelClose>().IsGraceful);
            TestDriver.Success(terminate);
            Assert.False(client.Completion.IsCompleted);
            close.Complete();
            TestDriver.Success(client.Completion);
            Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(pending));
            peer.AssertStopped();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StopUnblocksBackpressuredCommandsRatherThanLeavingCallersHung(bool dedicated)
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect(dedicated: dedicated);
            TestDriver.Success(client.SessionLock());
            var blockedWrite = peer.Wire.NextWrite();
            blockedWrite.Decode<NowMsgSessionLock>();
            var commands = Enumerable.Range(0, 2048).Select(_ => client.SessionLock()).ToArray();
            Assert.Contains(commands, task => !task.IsCompleted);
            TestDriver.Success(client.StopAsync());
            TestDriver.Until(() => commands.All(command => command.IsCompleted),
                $"all queued commands after StopAsync ({commands.Count(command => !command.IsCompleted)} pending)");
            foreach (var command in commands)
            {
                _ = command.Exception;
            }
            Assert.Contains(commands, task => !task.IsCompletedSuccessfully);
            peer.AssertStopped();
        });
    }

    [Fact]
    public void RdmSyncTimeoutIsTenSecondsAndDoesNotKillClient()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect();
            var elapsed = Stopwatch.StartNew();
            var sync = client.RdmSync();
            peer.Wire.Receive<NowMsgRdmCapabilities>();
            Assert.IsType<TimeoutException>(TestDriver.Failure(sync, TimeSpan.FromSeconds(15)));
            Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(9));
            Assert.False(client.Completion.IsCompleted);
            TestDriver.Success(client.SessionLock());
            peer.Wire.Receive<NowMsgSessionLock>();

            // Responses carry no request id: the late answer to the timed-out request must be matched to that request,
            // not complete the retry, whose own answer would otherwise be dropped.
            var retry = client.RdmSync();
            peer.Wire.Receive<NowMsgRdmCapabilities>();
            peer.Wire.Send(RdmCapabilities("1.0.0.0"));
            TestDriver.Success(client.SessionLock());
            peer.Wire.Receive<NowMsgSessionLock>();
            Assert.False(retry.IsCompleted);
            peer.Wire.Send(RdmCapabilities("2.0.0.0"));
            TestDriver.Success(retry);
            Assert.Equal("2.0.0.0", TestDriver.Success(client.GetRdmVersion())?.Version);
        }, timeoutSeconds: 40);
    }

    [Fact]
    public void ConcurrentPoolRdmSyncRequestsAreMatchedToResponsesInOrder()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect(dedicated: false);
            var first = client.RdmSync();
            peer.Wire.Receive<NowMsgRdmCapabilities>();
            var second = client.RdmSync();
            peer.Wire.Receive<NowMsgRdmCapabilities>();

            peer.Wire.Send(RdmCapabilities("1.0.0.0"));
            TestDriver.Success(first);
            TestDriver.Success(client.SessionLock());
            peer.Wire.Receive<NowMsgSessionLock>();
            Assert.False(second.IsCompleted);

            peer.Wire.Send(RdmCapabilities("2.0.0.0"));
            TestDriver.Success(second);
            Assert.Equal("2.0.0.0", TestDriver.Success(client.GetRdmVersion())?.Version);
            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        });
    }

    private static NowMsgRdmCapabilities RdmCapabilities(string version) =>
        new NowMsgRdmCapabilities.Builder(1672531200, version).WithAppAvailable().Build();

    [Fact]
    public void DirectRdmSyncRejectsAPeerWithoutRdmSupportBeforeQueueingARequest()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var connection = peer.BeginConnect();
            peer.Wire.Receive<NowMsgChannelCapset>();
            // The builder pins the current version, so the peer's 1.2 is patched into the wire frame (header is 8 bytes).
            var capset = WireTransport.Encode(ClientPeer.Capabilities());
            BitConverter.TryWriteBytes(capset.AsSpan(8), (ushort)1);
            BitConverter.TryWriteBytes(capset.AsSpan(10), (ushort)2);
            peer.Wire.NextRead().Complete(capset);
            var client = peer.FinishConnect(connection);
            Assert.Equal(new NowProtoVersion(1, 2), client.Capabilities.Version);

            Assert.IsType<NowClientException>(TestDriver.Failure(client.RdmSync()));
            Assert.IsType<NowClientException>(TestDriver.Failure(client.IsRdmAppAvailable()));

            // Nothing went on the wire, so a later supported command is not stuck behind an unanswerable request.
            TestDriver.Success(client.SessionLock());
            peer.Wire.Receive<NowMsgSessionLock>();
            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        });
    }
}