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

    // Any received message counts as liveness, so the only continuously ready I/O that must still let the
    // expiry timer run is outbound: a peer that accepts writes but sends nothing is treated as gone.
    [Fact]
    public void ContinuouslyReadyWritesDoNotStarveNegotiatedHeartbeatExpiry()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect(heartbeat: TimeSpan.FromSeconds(5));
            var elapsed = Stopwatch.StartNew();
            TestDriver.Success(client.SessionLock());
            var heldWrite = peer.Wire.NextWrite();
            heldWrite.Decode<NowMsgSessionLock>();
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
            });
            heldWrite.Complete();

            // Checks run every 10s; allow two checks and 5s scheduling slack.
            TestDriver.Completed(client.Completion, TimeSpan.FromSeconds(25));
            Assert.True(ready.IsActive, "Heartbeat expiry must win before the ready-I/O watchdog.");
            Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(9),
                "The negotiated 5s interval plus 5s leeway must not expire early.");
            Assert.True(ready.Writes > 100, "Commands must stay ready throughout the heartbeat wait.");
            peer.AssertStopped();
        }, timeoutSeconds: 45);
    }

    [Fact]
    public void InboundTrafficWithoutHeartbeatsKeepsTheChannelAlive()
    {
        TestDriver.Run(() =>
        {
            using var peer = new ClientPeer();
            var client = peer.Connect(heartbeat: TimeSpan.FromSeconds(5));
            long notifications = 0;
            TestDriver.Success(client.SetRdmAppNotifyHandler((_, _, _) => Interlocked.Increment(ref notifications)));
            var frame = new NowMsgRdmAppNotify(NowRdmAppState.Ready, NowRdmReason.NotSpecified, "not-a-heartbeat");

            // Well past interval + leeway, with a non-heartbeat message every 3 s and no heartbeat PDU at all.
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(16))
            {
                peer.Wire.Send(frame);
                Thread.Sleep(3000);
                Assert.False(client.Completion.IsCompleted, $"The channel expired after {elapsed.Elapsed} despite inbound traffic.");
            }

            Assert.True(Interlocked.Read(ref notifications) >= 5);
            TestDriver.Success(client.StopAsync());
            peer.AssertStopped();
        }, timeoutSeconds: 45);
    }

    private static void QueueLocks(Client client, int count)
    {
        var submissions = Enumerable.Range(0, count).Select(_ => client.SessionLock()).ToArray();
        foreach (var submission in submissions)
            TestDriver.Success(submission);
    }
}