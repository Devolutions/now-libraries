namespace Devolutions.NowProto.Tests
{
    public class MsgExecGeneral
    {
        [Fact]
        public void Abort()
        {
            var msg = new NowMsgExecAbort(0x12345678, 1);

            var encoded = new byte[]
            {
                0x08, 0x00, 0x00, 0x00, 0x13, 0x01, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x00, 0x00, 0x00,
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.ExitCode, decoded.ExitCode);
        }

        [Fact]
        public void CancelReq()
        {
            var msg = new NowMsgExecCancelReq(0x12345678);

            var encoded = new byte[]
            {
                0x04, 0x00, 0x00, 0x00, 0x13, 0x02, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12,
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
        }

        [Fact]
        public void CancelRsp()
        {
            var msg = NowMsgExecCancelRsp.Success(0x12345678);

            var encoded = new byte[] {
                0x0E, 0x00, 0x00, 0x00, 0x13, 0x03, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.True(msg.IsSuccess);
            msg.ThrowIfError();
        }

        [Fact]
        public void CancelRspError()
        {
            var msg = NowMsgExecCancelRsp.Error(0x12345678, new NowGenericException(0xDEADBEEF, null));

            var encoded = new byte[] {
                0x0E, 0x00, 0x00, 0x00, 0x13, 0x03, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x00, 0x00, 0x00, 0xEF, 0xBE, 0xAD, 0xDE,
                0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.False(msg.IsSuccess);
            Assert.Throws<NowGenericException>(() => msg.ThrowIfError());
        }

        [Fact]
        public void Result()
        {
            var msg = NowMsgExecResult.Success(0x12345678, 42);

            var encoded = new byte[]
            {
                0x12, 0x00, 0x00, 0x00, 0x13, 0x04, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.True(msg.IsSuccess);
            Assert.Equal((uint)42, msg.GetExitCodeOrThrow());
        }

        [Fact]
        public void ResultError()
        {
            var msg = NowMsgExecResult.Error(
                0x12345678,
                new NowGenericException(0xDEADBEEF, "ABC")
            );

            var encoded = new byte[]
            {
                0x15, 0x00, 0x00, 0x00, 0x13, 0x04, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00,
                0xEF, 0xBE, 0xAD, 0xDE, 0x03, 0x41, 0x42, 0x43, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.False(msg.IsSuccess);
            Assert.Throws<NowGenericException>(() => msg.GetExitCodeOrThrow());
        }

        [Fact]
        public void Data()
        {
            var msg = new NowMsgExecData(
                0x12345678,
                NowMsgExecData.StreamKind.Stdout,
                true,
                new byte[] { 0x01, 0x02, 0x03 }
            );

            var encoded = new byte[]
            {
                0x08, 0x00, 0x00, 0x00, 0x13, 0x05, 0x05, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x03, 0x01, 0x02, 0x03
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Stream, decoded.Stream);
            Assert.True(msg.Last);
            Assert.Equal(msg.Data, decoded.Data);
        }

        [Fact]
        public void DataEmpty()
        {
            var msg = new NowMsgExecData(
                0x12345678,
                NowMsgExecData.StreamKind.Stdin,
                false,
                ArraySegment<byte>.Empty
            );

            var encoded = new byte[]
            {
                0x05, 0x00, 0x00, 0x00, 0x13, 0x05, 0x02, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Stream, decoded.Stream);
            Assert.False(msg.Last);
            Assert.Equal(0, msg.Data.Count);
        }

        [Fact]
        public void Started()
        {
            var msg = new NowMsgExecStarted(0x12345678);

            var encoded = new byte[]
            {
                0x04, 0x00, 0x00, 0x00, 0x13, 0x06, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12,
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
        }
    }
}