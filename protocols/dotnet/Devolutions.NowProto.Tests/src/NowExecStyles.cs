namespace Devolutions.NowProto.Tests
{
    public class NowExecStyles
    {

        [Fact]
        public void DecodeRunV1_0()
        {
            var encoded = new byte[]
            {
                0x0B, 0x00, 0x00, 0x00, 0x13, 0x10, 0x00, 0x00, 0x67, 0x45,
                0x23, 0x01, 0x05, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x00
            };

            var readCursor = new NowReadCursor(encoded);
            var decoded = NowMessage.Read(readCursor).Deserialize<NowMsgExecRun>();

            Assert.Equal((uint)0x1234567, decoded.SessionId);
            Assert.Equal("hello", decoded.Command);
        }

        [Fact]
        public void Run()
        {
            var msg = new NowMsgExecRun.Builder(0x1234567, "hello")
                .Directory("hi")
                .Build();

            var encoded = new byte[]
            {
                0x0F, 0x00, 0x00, 0x00, 0x13, 0x10, 0x01, 0x00, 0x67, 0x45,
                0x23, 0x01, 0x05, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x00, 0x02,
                0x68, 0x69, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Command, decoded.Command);
        }

        [Fact]
        public void Process()
        {
            var msg = new NowMsgExecProcess.Builder(0x12345678, "a")
                .Parameters("b")
                .Directory("c")
                .Build();

            var encoded = new byte[]
            {
                0x0D, 0x00, 0x00, 0x00, 0x13, 0x11, 0x03, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x61, 0x00, 0x01, 0x62, 0x00, 0x01, 0x63,
                0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Filename, decoded.Filename);
            Assert.Equal(msg.Parameters, decoded.Parameters);
            Assert.Equal(msg.Directory, decoded.Directory);
        }

        [Fact]
        public void ProcessSimple()
        {
            var msg = new NowMsgExecProcess.Builder(0x12345678, "a").Build();

            var encoded = new byte[]
            {
                0x0B, 0x00, 0x00, 0x00, 0x13, 0x11, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Filename, decoded.Filename);
            Assert.Null(decoded.Parameters);
            Assert.Null(decoded.Directory);
        }

        [Fact]
        public void Shell()
        {
            var msg = new NowMsgExecShell.Builder(0x12345678, "a")
                .Shell("b")
                .Directory("c")
                .Build();

            var encoded = new byte[]
            {
                0x0D, 0x00, 0x00, 0x00, 0x13, 0x12, 0x03, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x61, 0x00, 0x01, 0x62, 0x00, 0x01, 0x63,
                0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Filename, decoded.Filename);
            Assert.Equal(decoded.Shell, decoded.Shell);
            Assert.Equal(decoded.Directory, decoded.Directory);
        }

        [Fact]
        public void ShellSimple()
        {
            var msg = new NowMsgExecShell.Builder(0x12345678, "a").Build();

            var encoded = new byte[]
            {
                0x0B, 0x00, 0x00, 0x00, 0x13, 0x12, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Filename, decoded.Filename);
            Assert.Null(decoded.Shell);
            Assert.Null(decoded.Directory);
        }

        [Fact]
        public void Batch()
        {
            var msg = new NowMsgExecBatch.Builder(0x12345678, "a")
                .Directory("b")
                .Build();

            var encoded = new byte[]
            {
                0x0A, 0x00, 0x00, 0x00, 0x13, 0x13, 0x01, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x61, 0x00, 0x01, 0x62, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Filename, decoded.Filename);
            Assert.Equal(msg.Directory, decoded.Directory);
        }

        [Fact]
        public void BatchSimple()
        {
            var msg = new NowMsgExecBatch.Builder(0x12345678, "a").Build();

            var encoded = new byte[]
            {
                0x09, 0x00, 0x00, 0x00, 0x13, 0x13, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x61, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Filename, decoded.Filename);
            Assert.Null(decoded.Directory);
        }

        [Fact]
        public void WinPs()
        {
            var msg = new NowMsgExecWinPs.Builder(0x12345678, "a")
                .ApartmentState(NowMsgExecWinPs.ApartmentStateKind.Mta)
                .SetNoProfile()
                .SetNoLogo()
                .Directory("d")
                .ExecutionPolicy("b")
                .ConfigurationName("c")
                .Build();

            var encoded = new byte[]
            {
                0x10, 0x00, 0x00, 0x00, 0x13, 0x14, 0xD9, 0x01, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x61, 0x00, 0x01, 0x64, 0x00, 0x01, 0x62,
                0x00, 0x01, 0x63, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Command, decoded.Command);
            Assert.Equal(msg.Directory, decoded.Directory);
            Assert.Equal(msg.ConfigurationName, decoded.ConfigurationName);
            Assert.Equal(msg.ExecutionPolicy, decoded.ExecutionPolicy);
            Assert.Equal(msg.ApartmentState, decoded.ApartmentState);
            Assert.True(decoded.NoProfile);
            Assert.True(decoded.NoLogo);
            Assert.False(decoded.NonInteractive);
            Assert.False(decoded.NoExit);
        }

        [Fact]
        public void WinPsSimple()
        {
            var msg = new NowMsgExecWinPs.Builder(0x12345678, "a").Build();

            var encoded = new byte[]
            {
                0x0D, 0x00, 0x00, 0x00, 0x13, 0x14, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Command, decoded.Command);
            Assert.Null(decoded.Directory);
            Assert.Null(decoded.ConfigurationName);
            Assert.Null(decoded.ExecutionPolicy);
            Assert.Null(decoded.ApartmentState);
            Assert.False(decoded.NoProfile);
            Assert.False(decoded.NoLogo);
            Assert.False(decoded.NonInteractive);
            Assert.False(decoded.NoExit);
        }

        [Fact]
        public void Pwsh()
        {
            var msg = new NowMsgExecPwsh.Builder(0x12345678, "a")
                .ApartmentState(NowMsgExecPwsh.ApartmentStateKind.Mta)
                .SetNoProfile()
                .SetNoLogo()
                .Directory("d")
                .ExecutionPolicy("b")
                .ConfigurationName("c")
                .Build();

            var encoded = new byte[]
            {
                0x10, 0x00, 0x00, 0x00, 0x13, 0x15, 0xD9, 0x01, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x61, 0x00, 0x01, 0x64, 0x00, 0x01, 0x62,
                0x00, 0x01, 0x63, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Command, decoded.Command);
            Assert.Equal(msg.Directory, decoded.Directory);
            Assert.Equal(msg.ConfigurationName, decoded.ConfigurationName);
            Assert.Equal(msg.ExecutionPolicy, decoded.ExecutionPolicy);
            Assert.Equal(msg.ApartmentState, decoded.ApartmentState);
            Assert.True(decoded.NoProfile);
            Assert.True(decoded.NoLogo);
            Assert.False(decoded.NonInteractive);
            Assert.False(decoded.NoExit);
        }

        [Fact]
        public void PwshSimple()
        {
            var msg = new NowMsgExecPwsh.Builder(0x12345678, "a").Build();

            var encoded = new byte[]
            {
                0x0D, 0x00, 0x00, 0x00, 0x13, 0x15, 0x00, 0x00, 0x78, 0x56,
                0x34, 0x12, 0x01, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.SessionId, decoded.SessionId);
            Assert.Equal(msg.Command, decoded.Command);
            Assert.Null(decoded.Directory);
            Assert.Null(decoded.ConfigurationName);
            Assert.Null(decoded.ExecutionPolicy);
            Assert.Null(decoded.ApartmentState);
            Assert.False(decoded.NoProfile);
            Assert.False(decoded.NoLogo);
            Assert.False(decoded.NonInteractive);
            Assert.False(decoded.NoExit);
        }
    }
}