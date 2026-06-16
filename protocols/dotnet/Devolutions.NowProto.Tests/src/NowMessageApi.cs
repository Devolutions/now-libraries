namespace Devolutions.NowProto.Tests
{
    public class NowMessageApi
    {
        [Fact]
        void DecodeAny()
        {
            var encoded = new byte[]
            {
                0x10, 0x00, 0x00, 0x00, 0x13, 0x15, 0xD9, 0x01, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x61, 0x00, 0x01, 0x64, 0x00, 0x01, 0x62,
                0x00, 0x01, 0x63, 0x00
            };

            var cursor = new NowReadCursor(encoded);
            var message = NowMessage.Read(cursor);

            Assert.Equal(0x13, message.MessageClass);
            Assert.Equal(0x15, message.MessageKind);
            Assert.Equal(0x10, message.Body.Length);
            Assert.Equal(0x01D9, message.Flags);

            var owned = message.ToOwned();
            _ = owned.Inspect().Deserialize<NowMsgExecPwsh>();

            var deserialized = message.Deserialize<NowMsgExecPwsh>();

            Assert.Equal("a", deserialized.Command);
        }
    }
}