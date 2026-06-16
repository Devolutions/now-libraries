using Devolutions.NowProto.Capabilities;

namespace Devolutions.NowProto.Tests
{
    public class MsgChannel
    {
        [Fact]
        void Capset()
        {
            var msg = new NowMsgChannelCapset.Builder()
                .ExecCapset(NowCapabilityExec.Run | NowCapabilityExec.Shell)
                .SystemCapset(NowCapabilitySystem.Shutdown)
                .SessionCapset(NowCapabilitySession.Msgbox)
                .HeartbeatInterval(TimeSpan.FromSeconds(300))
                .Build();

            var encoded = new byte[]
            {
                0x0E, 0x00, 0x00, 0x00, 0x10, 0x01, 0x01, 0x00, 0x01, 0x00, 0x06, 0x00, 0x01, 0x00, 0x04, 0x00, 0x05, 0x00, 0x2C, 0x01, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.Version.Major, decoded.Version.Major);
            Assert.Equal(msg.Version.Minor, decoded.Version.Minor);
            Assert.Equal(msg.SystemCapset, decoded.SystemCapset);
            Assert.Equal(msg.SessionCapset, decoded.SessionCapset);
            Assert.Equal(msg.ExecCapset, decoded.ExecCapset);
            Assert.NotNull(decoded.HeartbeatInterval);
            Assert.Equal((uint)(msg.HeartbeatInterval?.TotalSeconds!), (uint)(decoded.HeartbeatInterval?.TotalSeconds!));
        }

        [Fact]
        public void CapsetSimple()
        {
            var msg = new NowMsgChannelCapset();

            var encoded = new byte[]
            {
                0x0E, 0x00, 0x00, 0x00, 0x10, 0x01, 0x00, 0x00, 0x01, 0x00, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(NowProtoVersion.Current.Major, decoded.Version.Major);
            Assert.Equal(NowProtoVersion.Current.Minor, decoded.Version.Minor);
            Assert.Equal(NowCapabilitySystem.None, decoded.SystemCapset);
            Assert.Equal(NowCapabilitySession.None, decoded.SessionCapset);
            Assert.Equal(NowCapabilityExec.None, decoded.ExecCapset);
            Assert.Null(decoded.HeartbeatInterval);
        }

        [Fact]
        public void CapsetTooSmallHeartbeatInterval()
        {
            Assert.Throws<NowEncodeException>(
                () => new NowMsgChannelCapset.Builder().HeartbeatInterval(TimeSpan.FromSeconds(3))
            );
        }

        [Fact]
        public void CapsetTooBigHeartbeatInterval()
        {
            Assert.Throws<NowEncodeException>(
                () => new NowMsgChannelCapset.Builder().HeartbeatInterval(TimeSpan.FromSeconds(60 * 60 * 25))
            );
        }

        [Fact]
        public void Heartbeat()
        {
            var original = new NowMsgChannelHeartbeat();

            var encoded = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x10, 0x02, 0x00, 0x00
            };

            _ = NowTest.MessageRoundtrip(original, encoded);
        }

        [Fact]
        public void ChannelCloseNormal()
        {
            var msg = NowMsgChannelClose.Graceful();

            var encoded = new byte[]
            {
                0x0A, 0x00, 0x00, 0x00, 0x10, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.True(decoded.IsGraceful);
            decoded.ThrowIfError();
        }

        [Fact]
        public void ChannelCloseError()
        {
            var msg = NowMsgChannelClose.Error(new NowGenericException(0, null));

            var encoded = new byte[]
            {
                0x0A, 0x00, 0x00, 0x00, 0x10, 0x03, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.False(decoded.IsGraceful);
            Assert.Throws<NowGenericException>(() => decoded.ThrowIfError());
        }
    }
}