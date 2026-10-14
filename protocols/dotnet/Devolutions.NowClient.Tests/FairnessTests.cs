using System.Diagnostics;

namespace Devolutions.NowClient.Tests;

public sealed class FairnessTests
{
    [Fact]
    public void ReadyCommandBacklogDoesNotStarveReceivedHeartbeatAndNotification()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect(heartbeat: TimeSpan.FromSeconds(5));
            long writes = 0;
            long writesAtNotification = -1;
            TestDriver.Success(client.SetRdmAppNotifyHandler((_, _, data) =>
            {
                if (data == "after-heartbeat")
                    Interlocked.Exchange(ref writesAtNotification, Interlocked.Read(ref writes));
            }));

            // Suspend command execution, not the protocol thread: public command
            // submissions can finish and preload the queue while this write waits.
            TestDriver.Success(client.SessionLock());
            var heldWrite = peer.Wire.NextWrite();
            heldWrite.Decode<NowMsgSessionLock>();
            var read = peer.Wire.NextRead();
            const int backlog = 128;
            QueueLocks(client, backlog);
            using var ready = peer.Wire.BeginReadyIo(TimeSpan.FromSeconds(10), bytes =>
            {
                NowMessage.Read(new NowReadCursor(bytes)).Deserialize<NowMsgSessionLock>();
                Interlocked.Increment(ref writes);
            });
            read.Complete(
            [
                .. WireTransport.Encode(new NowMsgChannelHeartbeat()),
                .. WireTransport.Encode(new NowMsgRdmAppNotify(
                    NowRdmAppState.Ready, NowRdmReason.NotSpecified, "after-heartbeat")),
            ]);
            heldWrite.Complete();

            TestDriver.Until(() => Interlocked.Read(ref writesAtNotification) >= 0,
                "notification behind heartbeat while commands remain ready");
            // This is an ordering assertion, not a race against a wall-clock
            // producer. Command-first selection drains all 128 before reading.
            Assert.InRange(Interlocked.Read(ref writesAtNotification), 0L, backlog / 2L);
            TestDriver.Until(() => ready.Writes == backlog, "all queued wire commands");
            Assert.False(client.Completion.IsCompleted);
            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        });
    }

    [Fact]
    public void ContinuouslyReadyIoDoesNotStarveNegotiatedHeartbeatExpiry()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect(heartbeat: TimeSpan.FromSeconds(5));
            var elapsed = Stopwatch.StartNew();
            long notifications = 0;
            TestDriver.Success(client.SetRdmAppNotifyHandler((_, _, _) =>
                Interlocked.Increment(ref notifications)));
            TestDriver.Success(client.SessionLock());
            var heldWrite = peer.Wire.NextWrite();
            heldWrite.Decode<NowMsgSessionLock>();
            var read = peer.Wire.NextRead();
            var frame = WireTransport.Encode(new NowMsgRdmAppNotify(
                NowRdmAppState.Ready, NowRdmReason.NotSpecified, "not-a-heartbeat"));
            QueueLocks(client, 8);

            // Replenish exactly one command per synchronous write, keeping the
            // backlog constant even if the raw driver does not get scheduled.
            // No callback waits, pool timer, or pool-based producer is involved.
            using var ready = peer.Wire.BeginReadyIo(TimeSpan.FromSeconds(30), bytes =>
            {
                NowMessage.Read(new NowReadCursor(bytes)).Deserialize<NowMsgSessionLock>();
                var replacement = client.SessionLock();
                Assert.True(replacement.IsCompleted, "Replenishment must not suspend on the protocol thread.");
                replacement.GetAwaiter().GetResult();
            }, frame);
            read.Complete(frame);
            heldWrite.Complete();

            // Wait for completion before asserting notification progress: an
            // unfair selector must fail the deadline, not only the first read.
            // Checks run every 10s; allow two checks and 5s scheduling slack.
            TestDriver.Completed(client.Completion, TimeSpan.FromSeconds(25));
            Assert.True(ready.IsActive, "Heartbeat expiry must win before the ready-I/O watchdog.");
            Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(9),
                "The negotiated 5s interval plus 5s leeway must not expire early.");
            Assert.True(ready.Writes > 100, "Commands must stay ready throughout the heartbeat wait.");
            Assert.True(ready.Reads > 100, "The peer must continuously supply ready non-heartbeat frames.");
            Assert.True(Interlocked.Read(ref notifications) > 100);
            peer.AssertStopped();
        }, timeoutSeconds: 45);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HeartbeatExpiryEndsTheConnectionWhileACommandWriteHangs(bool dedicated)
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect(dedicated: dedicated, heartbeat: TimeSpan.FromSeconds(5));
            var elapsed = Stopwatch.StartNew();
            TestDriver.Success(client.SessionLock());
            var hungWrite = peer.Wire.NextWrite();
            hungWrite.Decode<NowMsgSessionLock>();

            // The peer never heartbeats and the write never completes; the watchdog must still fault the worker,
            // and shutdown then disposes the transport, which releases the hung write.
            Assert.IsType<NowClientException>(TestDriver.Failure(client.Completion, TimeSpan.FromSeconds(25)));
            Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(9),
                "The negotiated 5s interval plus 5s leeway must not expire early.");
            Assert.IsType<ObjectDisposedException>(TestDriver.Failure(hungWrite.Task));
            peer.AssertStopped(successful: false);
        }, timeoutSeconds: 45);
    }

    private static void QueueLocks(Client client, int count)
    {
        var submissions = Enumerable.Range(0, count).Select(_ => client.SessionLock()).ToArray();
        foreach (var submission in submissions)
            TestDriver.Success(submission);
    }
}