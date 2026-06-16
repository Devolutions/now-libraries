namespace Devolutions.NowProto.Tests
{
    internal class NowTest
    {
        internal static T MessageRoundtrip<T>(T original, byte[] expectedEncoded) where T : INowSerialize, INowDeserialize<T>
        {
            var actualEncoded = new byte[(original as INowSerialize).Size];

            var writeCursor = new NowWriteCursor(actualEncoded);
            (original as INowSerialize).Serialize(writeCursor);

            Assert.Equal(expectedEncoded, actualEncoded);

            var readCursor = new NowReadCursor(expectedEncoded);
            return NowMessage.Read(readCursor).Deserialize<T>();
        }
    }
}