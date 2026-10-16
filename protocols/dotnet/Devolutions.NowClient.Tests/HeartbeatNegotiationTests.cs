using System.Diagnostics;

namespace Devolutions.NowClient.Tests;

public sealed class HeartbeatNegotiationTests
{
    // Deployed Agents answer with a default capset carrying no interval; by default that means no watchdog.
    // Zero and oversized values arrive from peers whose builders are less strict than ours, so they are decoded.
    [Theory]
    [InlineData(null)]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void NegotiationAcceptsAbsentZeroAndOversizedPeerIntervals(uint? peerSeconds)
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = ConnectWithPeerInterval(peer, peerSeconds);
            Assert.Equal(peerSeconds == null ? null : TimeSpan.FromSeconds(peerSeconds.Value), client.Capabilities.HeartbeatInterval);
            TestDriver.Success(client.SessionLock());
            peer.Wire.Receive<NowMsgSessionLock>();
            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        });
    }

    // The worker enforces the smaller of the two specified intervals: a 5 s peer expires at interval + 5 s leeway
    // on the next 2 × interval check, while the client's own 60 s would not fire within this test.
    [Fact]
    public void PeerIntervalBelowTheClientsIsEnforced()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var elapsed = Stopwatch.StartNew();
            var client = ConnectWithPeerInterval(peer, 5);
            TestDriver.Success(client.Completion, TimeSpan.FromSeconds(25));
            Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(9));
            peer.AssertStopped();
        }, timeoutSeconds: 45);
    }

    // The Agent fixed periodic heartbeats and interval advertisement together, so "no interval" identifies an Agent
    // whose heartbeat is an inactivity timer; enforcing the client's request against it would drop busy channels.
    [Fact]
    public void AbsentPeerIntervalDisablesTheWatchdogByDefault()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = ConnectWithPeerInterval(peer, null, requestedHeartbeat: TimeSpan.FromSeconds(5));
            Thread.Sleep(TimeSpan.FromSeconds(12));
            Assert.False(client.Completion.IsCompleted, "An absent peer interval must not arm the client's own watchdog.");
            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        }, timeoutSeconds: 45);
    }

    [Fact]
    public void AbsentPeerIntervalEnforcesTheRequestOnlyWhenOptedIn()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var elapsed = Stopwatch.StartNew();
            var client = ConnectWithPeerInterval(peer, null, requestedHeartbeat: TimeSpan.FromSeconds(5),
                policy: NowClientHeartbeatPolicy.EnforceRequested);
            TestDriver.Success(client.Completion, TimeSpan.FromSeconds(25));
            Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(9));
            peer.AssertStopped();
        }, timeoutSeconds: 45);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(24 * 60 * 60 + 1)]
    public void RequestedIntervalOutsideTheProtocolRangeIsRejectedBeforeAnyIo(int seconds)
    {
        TestDriver.Run(() =>
        {
            using var wire = new WireTransport();
            Exception? rejected = null;
            try
            {
                _ = Client.Connect(wire, new NowClientOptions { RequestedHeartbeatInterval = TimeSpan.FromSeconds(seconds) });
            }
            catch (Exception exception)
            {
                rejected = exception;
            }

            Assert.IsType<ArgumentOutOfRangeException>(rejected);
            Assert.Empty(wire.Calls);
            Assert.False(wire.IsDisposed);
        });
    }

    [Fact]
    public void ZeroPeerIntervalDisablesTheWatchdog()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = ConnectWithPeerInterval(peer, 0);
            Thread.Sleep(TimeSpan.FromSeconds(12));
            Assert.False(client.Completion.IsCompleted, "A zero interval means heartbeats are disabled.");
            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        }, timeoutSeconds: 45);
    }

    private static Client ConnectWithPeerInterval(ClientPeer peer, uint? peerSeconds, TimeSpan? requestedHeartbeat = null,
        NowClientHeartbeatPolicy policy = NowClientHeartbeatPolicy.FollowPeer)
    {
        var connection = peer.BeginConnect(requestedHeartbeat: requestedHeartbeat, unspecifiedPeerHeartbeat: policy);
        peer.Wire.Receive<NowMsgChannelCapset>();
        peer.Wire.NextRead().Complete(WireTransport.Encode(PeerCapset(peerSeconds)));
        return peer.FinishConnect(connection);
    }

    // The builder refuses zero and anything over 24 h, so the wire value is patched in after serialization.
    private static NowMsgChannelCapset PeerCapset(uint? heartbeatSeconds)
    {
        if (heartbeatSeconds == null)
            return ClientPeer.Capabilities();

        var bytes = WireTransport.Encode(ClientPeer.Capabilities(TimeSpan.FromSeconds(60)));
        BitConverter.TryWriteBytes(bytes.AsSpan(bytes.Length - 4), heartbeatSeconds.Value);
        return NowMessage.Read(new NowReadCursor(bytes)).Deserialize<NowMsgChannelCapset>();
    }
}