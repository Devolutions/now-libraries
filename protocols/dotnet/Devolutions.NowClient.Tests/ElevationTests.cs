namespace Devolutions.NowClient.Tests;

public sealed class ElevationTests
{
    [Fact]
    public void ElevatedRequestIsRefusedWhenThePeerDoesNotAdvertiseElevation()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var connection = peer.BeginConnect();
            peer.Wire.Receive<NowMsgChannelCapset>();
            peer.Wire.Send(new NowMsgChannelCapset.Builder()
                .SystemCapset(NowCapabilitySystem.All)
                .SessionCapset(NowCapabilitySession.All)
                .ExecCapset(NowCapabilityExec.All & ~NowCapabilityExec.Elevation)
                .Build());
            var client = peer.FinishConnect(connection);
            Assert.False(client.Capabilities.ExecCapset.HasFlag(NowCapabilityExec.Elevation));

            Assert.IsType<NowClientException>(TestDriver.Failure(client.ExecRun(new ExecRunParams("run.exe").Elevated())));
            Assert.IsType<NowClientException>(TestDriver.Failure(client.ExecProcess(new ExecProcessParams("process.exe").Elevated())));

            // Nothing went on the wire, so a later supported command is not stuck behind a request the peer would downgrade.
            TestDriver.Success(client.SessionLock());
            peer.Wire.Receive<NowMsgSessionLock>();
            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        });
    }

    [Fact]
    public void ElevatedBatchReachesThePeerWithElevatedAndNoExitButWithoutIoRedirection()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect();
            var session = TestDriver.Success(client.ExecBatch(new ExecBatchParams("whoami").Elevated().NoExit()));

            var request = peer.Wire.Receive<NowMsgExecBatch>();
            Assert.Equal(session.SessionId, request.SessionId);
            Assert.True(request.Elevated);
            Assert.True(request.NoExit);
            Assert.False(request.IoRedirection);

            peer.Wire.Send(NowMsgExecResult.Success(session.SessionId, 42));
            Assert.Equal(42u, TestDriver.Success(session.GetResult()));
            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        });
    }

    [Fact]
    public void BatchNoExitIsRefusedOnAPeerOlderThan17()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var connection = peer.BeginConnect();
            peer.Wire.Receive<NowMsgChannelCapset>();
            // The builder pins the current version, so the peer's 1.6 is patched into the wire frame (header is 8 bytes).
            var capset = WireTransport.Encode(ClientPeer.Capabilities());
            BitConverter.TryWriteBytes(capset.AsSpan(8), (ushort)1);
            BitConverter.TryWriteBytes(capset.AsSpan(10), (ushort)6);
            peer.Wire.NextRead().Complete(capset);
            var client = peer.FinishConnect(connection);
            Assert.Equal(new NowProtoVersion(1, 6), client.Capabilities.Version);

            Assert.IsType<NowClientException>(TestDriver.Failure(client.ExecBatch(new ExecBatchParams("whoami").NoExit())));

            // Without NoExit the same request is valid for a 1.6 peer.
            var session = TestDriver.Success(client.ExecBatch(new ExecBatchParams("whoami")));
            var request = peer.Wire.Receive<NowMsgExecBatch>();
            Assert.Equal(session.SessionId, request.SessionId);
            Assert.False(request.NoExit);
            peer.Wire.Send(NowMsgExecResult.Success(session.SessionId, 0));
            Assert.Equal(0u, TestDriver.Success(session.GetResult()));
            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        });
    }
}