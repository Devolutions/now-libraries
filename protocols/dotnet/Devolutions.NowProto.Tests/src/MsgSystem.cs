namespace Devolutions.NowProto.Tests
{
    public class MsgSystem
    {
        [Fact]
        public void Shutdown()
        {
            var msg = new NowMsgSystemShutdown.Builder()
                .Message("hello")
                .Timeout(TimeSpan.FromSeconds(123))
                .Force(true)
                .Build();

            var encoded = new byte[]
            {
                0x0B, 0x00, 0x00, 0x00, 0x11, 0x03, 0x01, 0x00, 0x7B, 0x00,
                0x00, 0x00, 0x05, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal("hello", decoded.Message);
            Assert.Equal(123, decoded.Timeout.TotalSeconds);
            Assert.True(decoded.Force);
            Assert.False(decoded.Reboot);
        }
    }
}