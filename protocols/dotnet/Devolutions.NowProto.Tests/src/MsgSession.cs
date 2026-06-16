namespace Devolutions.NowProto.Tests
{
    public class MsgSession
    {
        [Fact]
        public void SessionLock()
        {
            var msg = new NowMsgSessionLock();

            var encoded = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x12, 0x01, 0x00, 0x00, };

            _ = NowTest.MessageRoundtrip(msg, encoded);
        }

        [Fact]
        public void SessionLogoff()
        {
            var msg = new NowMsgSessionLogoff();

            var encoded = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x12, 0x02, 0x00, 0x00, };

            _ = NowTest.MessageRoundtrip(msg, encoded);
        }

        [Fact]
        public void MsgBoxReq()
        {
            var msg = new NowMsgSessionMessageBoxReq.Builder(0x76543210, "hello")
                .WithResponse()
                .Style(NowMsgSessionMessageBoxReq.MessageBoxStyle.AbortRetryIgnore)
                .Title("world")
                .Timeout(TimeSpan.FromSeconds(3))
                .Build();

            var encoded = new byte[]
            {
                0x1A, 0x00, 0x00, 0x00, 0x12, 0x03, 0x0F, 0x00, 0x10, 0x32, 0x54, 0x76,
                0x02, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x05, 0x77, 0x6F, 0x72,
                0x6C, 0x64, 0x00, 0x05, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.RequestId, decoded.RequestId);
            Assert.Equal(msg.Message, decoded.Message);
            Assert.True(decoded.WaitForResponse);
            Assert.Equal(msg.Style, decoded.Style);
            Assert.Equal(msg.Title, decoded.Title);
            Assert.Equal(msg.Timeout, decoded.Timeout);
        }

        [Fact]
        public void MsgBoxReqSimple()
        {
            var msg = new NowMsgSessionMessageBoxReq
                .Builder(0x76543210, "hello")
                .Build();



            var encoded = new byte[]
            {
                0x15, 0x00, 0x00, 0x00, 0x12, 0x03, 0x00, 0x00, 0x10, 0x32, 0x54, 0x76,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x68,
                0x65, 0x6C, 0x6C, 0x6F, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.RequestId, decoded.RequestId);
            Assert.Equal(msg.Message, decoded.Message);
            Assert.False(decoded.WaitForResponse);
            Assert.Null(decoded.Style);
            Assert.Null(decoded.Title);
            Assert.Null(decoded.Timeout);
        }

        [Fact]
        public void MsgBoxRsp()
        {
            var msg = NowMsgSessionMessageBoxRsp.Success(
                0x01234567,
                NowMsgSessionMessageBoxRsp.MessageBoxResponse.Retry
            );

            var encoded = new byte[]
            {
                0x12, 0x00, 0x00, 0x00, 0x12, 0x04, 0x00, 0x00, 0x67, 0x45, 0x23, 0x01,
                0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.RequestId, decoded.RequestId);
            Assert.True(msg.IsSuccess);
            Assert.Equal(NowMsgSessionMessageBoxRsp.MessageBoxResponse.Retry, msg.GetResponseOrThrow());
        }

        [Fact]
        public void MsgBoxRspError()
        {
            var msg = NowMsgSessionMessageBoxRsp.Error(
                0x01234567,
                new NowProtocolException(NowProtocolErrorCode.NotImplemented)
            );

            var encoded = new byte[]
            {
                0x12, 0x00, 0x00, 0x00, 0x12, 0x04, 0x00, 0x00, 0x67, 0x45, 0x23, 0x01,
                0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x07, 0x00, 0x00, 0x00,
                0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.RequestId, decoded.RequestId);
            Assert.False(msg.IsSuccess);
            Assert.Throws<NowProtocolException>(() => msg.GetResponseOrThrow());
        }

        [Fact]
        public void SetKbdLayoutSpecific()
        {
            var msg = NowMsgSessionSetKbdLayout.Specific("00000409");

            var encoded = new byte[]
            {
                0x0A, 0x00, 0x00, 0x00, 0x12, 0x05, 0x00, 0x00, 0x08, 0x30, 0x30, 0x30,
                0x30, 0x30, 0x34, 0x30, 0x39, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.LayoutOption, decoded.LayoutOption);
            Assert.Equal(msg.Layout, decoded.Layout);

            Assert.Equal(NowMsgSessionSetKbdLayout.SetKbdLayoutOption.Specific, msg.LayoutOption);
            Assert.Equal("00000409", msg.Layout);
        }

        [Fact]
        public void SetKbdLayoutNext()
        {
            var msg = NowMsgSessionSetKbdLayout.Next();

            var encoded = new byte[]
            {
                0x02, 0x00, 0x00, 0x00, 0x12, 0x05, 0x01, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.LayoutOption, decoded.LayoutOption);
            Assert.Equal(msg.Layout, decoded.Layout);

            Assert.Equal(NowMsgSessionSetKbdLayout.SetKbdLayoutOption.Next, msg.LayoutOption);
            Assert.Null(msg.Layout);
        }

        [Fact]
        public void SetKbdLayoutPrev()
        {
            var msg = NowMsgSessionSetKbdLayout.Prev();

            var encoded = new byte[]
            {
                0x02, 0x00, 0x00, 0x00, 0x12, 0x05, 0x02, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(msg.LayoutOption, decoded.LayoutOption);
            Assert.Equal(msg.Layout, decoded.Layout);

            Assert.Equal(NowMsgSessionSetKbdLayout.SetKbdLayoutOption.Prev, msg.LayoutOption);
            Assert.Null(msg.Layout);
        }

        [Fact]
        public void WindowRecStartSimple()
        {
            var msg = new NowMsgSessionWindowRecStart(1000, trackTitleChange: false);

            var encoded = new byte[]
            {
                0x04, 0x00, 0x00, 0x00, 0x12, 0x06, 0x00, 0x00, 0xE8, 0x03, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(1000u, decoded.PollInterval);
            Assert.False(decoded.IsTrackTitleChange);
        }

        [Fact]
        public void WindowRecStartWithFlags()
        {
            var msg = new NowMsgSessionWindowRecStart(2000, trackTitleChange: true);

            var encoded = new byte[]
            {
                0x04, 0x00, 0x00, 0x00, 0x12, 0x06, 0x01, 0x00, 0xD0, 0x07, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(2000u, decoded.PollInterval);
            Assert.True(decoded.IsTrackTitleChange);
        }

        [Fact]
        public void WindowRecStop()
        {
            var msg = new NowMsgSessionWindowRecStop();

            var encoded = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x12, 0x07, 0x00, 0x00
            };

            _ = NowTest.MessageRoundtrip(msg, encoded);
        }

        [Fact]
        public void WindowRecEventActiveWindow()
        {
            var msg = NowMsgSessionWindowRecEvent.ActiveWindow(
                timestamp: 1732550400, // Unix timestamp: 2024-11-25 12:00:00 UTC
                processId: 1234,
                title: "Notepad",
                executablePath: "C:\\Windows\\System32\\notepad.exe"
            );

            var encoded = new byte[]
            {
                0x36, 0x00, 0x00, 0x00, 0x12, 0x08, 0x01, 0x00, 0x00, 0x9F, 0x44, 0x67,
                0x00, 0x00, 0x00, 0x00, 0xD2, 0x04, 0x00, 0x00, 0x07, 0x4E, 0x6F, 0x74,
                0x65, 0x70, 0x61, 0x64, 0x00, 0x1F, 0x43, 0x3A, 0x5C, 0x57, 0x69, 0x6E,
                0x64, 0x6F, 0x77, 0x73, 0x5C, 0x53, 0x79, 0x73, 0x74, 0x65, 0x6D, 0x33,
                0x32, 0x5C, 0x6E, 0x6F, 0x74, 0x65, 0x70, 0x61, 0x64, 0x2E, 0x65, 0x78,
                0x65, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(1732550400ul, decoded.Timestamp);
            Assert.Equal(WindowRecEventKind.ActiveWindow, decoded.Kind);

            var data = decoded.GetActiveWindowData();
            Assert.Equal(1234u, data.ProcessId);
            Assert.Equal("Notepad", data.Title);
            Assert.Equal("C:\\Windows\\System32\\notepad.exe", data.ExecutablePath);
        }

        [Fact]
        public void WindowRecEventTitleChanged()
        {
            var msg = NowMsgSessionWindowRecEvent.TitleChanged(
                timestamp: 1732550460, // Unix timestamp: 2024-11-25 12:01:00 UTC
                title: "Notepad - Document.txt"
            );

            var encoded = new byte[]
            {
                0x26, 0x00, 0x00, 0x00, 0x12, 0x08, 0x02, 0x00, 0x3C, 0x9F, 0x44, 0x67,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x16, 0x4E, 0x6F, 0x74,
                0x65, 0x70, 0x61, 0x64, 0x20, 0x2D, 0x20, 0x44, 0x6F, 0x63, 0x75, 0x6D,
                0x65, 0x6E, 0x74, 0x2E, 0x74, 0x78, 0x74, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(1732550460ul, decoded.Timestamp);
            Assert.Equal(WindowRecEventKind.TitleChanged, decoded.Kind);

            var data = decoded.GetTitleChangedData();
            Assert.Equal("Notepad - Document.txt", data.Title);
        }

        [Fact]
        public void WindowRecEventNoActiveWindow()
        {
            var msg = NowMsgSessionWindowRecEvent.NoActiveWindow(
                timestamp: 1732550520 // Unix timestamp: 2024-11-25 12:02:00 UTC
            );

            var encoded = new byte[]
            {
                0x10, 0x00, 0x00, 0x00, 0x12, 0x08, 0x04, 0x00, 0x78, 0x9F, 0x44, 0x67,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(1732550520ul, decoded.Timestamp);
            Assert.Equal(WindowRecEventKind.NoActiveWindow, decoded.Kind);
        }

        [Fact]
        public void WindowRecEventEmptyStrings()
        {
            var msg = NowMsgSessionWindowRecEvent.ActiveWindow(
                timestamp: 1732550400,
                processId: 5678,
                title: "",
                executablePath: ""
            );

            var encoded = new byte[]
            {
                0x10, 0x00, 0x00, 0x00, 0x12, 0x08, 0x01, 0x00, 0x00, 0x9F, 0x44, 0x67,
                0x00, 0x00, 0x00, 0x00, 0x2E, 0x16, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };

            var decoded = NowTest.MessageRoundtrip(msg, encoded);

            Assert.Equal(1732550400ul, decoded.Timestamp);
            Assert.Equal(WindowRecEventKind.ActiveWindow, decoded.Kind);

            var data = decoded.GetActiveWindowData();
            Assert.Equal(5678u, data.ProcessId);
            Assert.Equal("", data.Title);
            Assert.Equal("", data.ExecutablePath);
        }
    }
}