using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

/// <summary>
/// Tests for the <c>NOW_BROKER</c> event channel frame protocol. The binary fixture is
/// shared with the Rust test suite so both implementations stay wire-compatible.
/// </summary>
public class EventChannelTests
{
    private static string FramesFixturePath =>
        Path.Combine(TestData.SamplesDir, "frames", "event-channel.frames.bin");

    [Fact]
    public void Shared_frame_fixture_decodes_to_expected_frames()
    {
        var bytes = File.ReadAllBytes(FramesFixturePath);

        var frames = DecodeAll(bytes);

        Assert.Collection(
            frames,
            f =>
            {
                var hello = Assert.IsType<EventFrame.Hello>(f);
                Assert.Equal(EventChannelProtocol.VersionMajor, hello.VersionMajor);
                Assert.Equal(EventChannelProtocol.VersionMinor, hello.VersionMinor);
            },
            f => Assert.IsType<EventFrame.StatusUpdated>(f),
            f => Assert.Equal("hello \u03c0\n", Assert.IsType<EventFrame.Stdout>(f).Data),
            f => Assert.Equal("oops\n", Assert.IsType<EventFrame.Stderr>(f).Data),
            f => Assert.Equal(4096u, Assert.IsType<EventFrame.StdoutOverflow>(f).BytesSkipped),
            f => Assert.Equal(16u, Assert.IsType<EventFrame.StderrOverflow>(f).BytesSkipped),
            f =>
            {
                var unknown = Assert.IsType<EventFrame.Unknown>(f);
                Assert.Equal(0x7fff, unknown.Kind);
                Assert.Equal([1, 2, 3], unknown.Body);
            },
            f => Assert.IsType<EventFrame.Finish>(f));

        // The fixture must round-trip byte-for-byte through the encoder.
        var reencoded = frames.SelectMany(f => f.Encode()).ToArray();
        Assert.Equal(bytes, reencoded);
    }

    [Fact]
    public void Decoder_handles_partial_input_byte_by_byte()
    {
        var bytes = File.ReadAllBytes(FramesFixturePath);

        var decoder = new EventFrameDecoder();
        var frames = new List<EventFrame>();
        foreach (var b in bytes)
        {
            decoder.Extend([b]);
            while (decoder.TryReadFrame(out var frame))
            {
                frames.Add(frame!);
            }
        }

        Assert.Equal(8, frames.Count);
        Assert.IsType<EventFrame.Hello>(frames[0]);
        Assert.IsType<EventFrame.Finish>(frames[^1]);
    }

    [Fact]
    public void Decoder_rejects_invalid_utf8_output_frames()
    {
        var decoder = new EventFrameDecoder();
        decoder.Extend([2, 0, 0, 0, 0x03, 0x00, 0xff, 0xfe]);

        Assert.Throws<EventFrameException>(() => decoder.TryReadFrame(out _));
    }

    [Fact]
    public void Decoder_rejects_oversized_frames()
    {
        var decoder = new EventFrameDecoder();
        var size = BitConverter.GetBytes((uint)EventChannelProtocol.MaxFrameBodyBytes + 1);
        decoder.Extend([size[0], size[1], size[2], size[3], 0x03, 0x00]);

        Assert.Throws<EventFrameException>(() => decoder.TryReadFrame(out _));
    }

    [Fact]
    public void Fixed_body_frames_reject_wrong_length()
    {
        Assert.Throws<EventFrameException>(() => EventFrame.DecodeBody((ushort)EventFrameKind.Hello, [1, 0]));
        Assert.Throws<EventFrameException>(() => EventFrame.DecodeBody((ushort)EventFrameKind.Finish, [0]));
    }

    [Fact]
    public void Encoder_rejects_oversized_stdout_data()
    {
        var frame = new EventFrame.Stdout(new string('a', EventChannelProtocol.MaxFrameBodyBytes + 1));

        Assert.Throws<EventFrameException>(() => frame.Encode());
    }

    private static List<EventFrame> DecodeAll(byte[] bytes)
    {
        var decoder = new EventFrameDecoder();
        decoder.Extend(bytes);
        var frames = new List<EventFrame>();
        while (decoder.TryReadFrame(out var frame))
        {
            frames.Add(frame!);
        }

        return frames;
    }
}