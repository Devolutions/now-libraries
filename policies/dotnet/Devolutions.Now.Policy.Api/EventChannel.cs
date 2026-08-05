using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Api;

/// <summary>Descriptor of a per-operation event channel returned in the execution response.</summary>
/// <remarks>
/// The channel is one-way (read-only from the client side) and carries the
/// <c>NOW_BROKER</c> event frame protocol: stdout/stderr data and status change
/// notifications. See <c>policies/docs/event-channel-protocol.md</c>.
/// </remarks>
public sealed class EventChannel
{
    /// <summary>Transport kind of the channel.</summary>
    [JsonPropertyName("Kind")]
    public EventChannelKind Kind { get; set; }

    /// <summary>
    /// Transport-specific path. For <see cref="EventChannelKind.LocalPipe"/> this is the
    /// pipe name/path the client should connect to.
    /// </summary>
    [JsonPropertyName("Path")]
    public string Path { get; set; } = "";
}

/// <summary>Constants of the <c>NOW_BROKER</c> event channel frame protocol.</summary>
public static class EventChannelProtocol
{
    /// <summary>Protocol major version advertised in the <c>Hello</c> frame.</summary>
    public const ushort VersionMajor = 1;

    /// <summary>Protocol minor version advertised in the <c>Hello</c> frame.</summary>
    public const ushort VersionMinor = 0;

    /// <summary>Size in bytes of the fixed frame header (<c>u32 frame_size</c> + <c>u16 frame_kind</c>).</summary>
    public const int FrameHeaderSize = 6;

    /// <summary>
    /// Maximum allowed frame body size in bytes. Protects decoders from unbounded
    /// buffering; encoders must split larger output into multiple frames.
    /// </summary>
    public const int MaxFrameBodyBytes = 65536;
}

/// <summary><c>NOW_BROKER</c> event frame kind discriminators.</summary>
public enum EventFrameKind : ushort
{
    /// <summary><c>NOW_BROKER_HELLO</c></summary>
    Hello = 0x0000,

    /// <summary><c>NOW_BROKER_STATUS_UPDATED</c></summary>
    StatusUpdated = 0x0001,

    /// <summary><c>NOW_BROKER_FINISH</c></summary>
    Finish = 0x0002,

    /// <summary><c>NOW_BROKER_STDOUT</c></summary>
    Stdout = 0x0003,

    /// <summary><c>NOW_BROKER_STDERR</c></summary>
    Stderr = 0x0004,

    /// <summary><c>NOW_BROKER_STDOUT_OVERFLOW</c></summary>
    StdoutOverflow = 0x0005,

    /// <summary><c>NOW_BROKER_STDERR_OVERFLOW</c></summary>
    StderrOverflow = 0x0006,
}

/// <summary>A decoded <c>NOW_BROKER</c> event frame.</summary>
public abstract class EventFrame
{
    private protected EventFrame()
    {
    }

    /// <summary>Frame kind discriminator for this frame.</summary>
    public abstract ushort Kind { get; }

    /// <summary>
    /// First frame on the channel; acknowledges that the transport is ready and
    /// advertises the protocol version.
    /// </summary>
    public sealed class Hello(ushort versionMajor, ushort versionMinor) : EventFrame
    {
        public ushort VersionMajor { get; } = versionMajor;

        public ushort VersionMinor { get; } = versionMinor;

        public override ushort Kind => (ushort)EventFrameKind.Hello;
    }

    /// <summary>
    /// The operation status changed and awaits a <c>StatusRequest</c> HTTP query.
    /// Deliberately carries no payload: complex data is queried over HTTP.
    /// </summary>
    public sealed class StatusUpdated : EventFrame
    {
        public override ushort Kind => (ushort)EventFrameKind.StatusUpdated;
    }

    /// <summary>New stdout data. Strictly UTF-8; character boundaries are guaranteed by the broker.</summary>
    public sealed class Stdout(string data) : EventFrame
    {
        public string Data { get; } = data;

        public override ushort Kind => (ushort)EventFrameKind.Stdout;
    }

    /// <summary>New stderr data. Strictly UTF-8; character boundaries are guaranteed by the broker.</summary>
    public sealed class Stderr(string data) : EventFrame
    {
        public string Data { get; } = data;

        public override ushort Kind => (ushort)EventFrameKind.Stderr;
    }

    /// <summary>
    /// The operation finished; query <c>StatusRequest</c> for details. The channel can be
    /// gracefully closed by the client after this frame.
    /// </summary>
    public sealed class Finish : EventFrame
    {
        public override ushort Kind => (ushort)EventFrameKind.Finish;
    }

    /// <summary>The client was too slow to read stdout and some data was truncated.</summary>
    public sealed class StdoutOverflow(uint bytesSkipped) : EventFrame
    {
        public uint BytesSkipped { get; } = bytesSkipped;

        public override ushort Kind => (ushort)EventFrameKind.StdoutOverflow;
    }

    /// <summary>The client was too slow to read stderr and some data was truncated.</summary>
    public sealed class StderrOverflow(uint bytesSkipped) : EventFrame
    {
        public uint BytesSkipped { get; } = bytesSkipped;

        public override ushort Kind => (ushort)EventFrameKind.StderrOverflow;
    }

    /// <summary>
    /// A frame with an unknown kind. Must be ignored by consumers to allow
    /// forward-compatible protocol extension.
    /// </summary>
    public sealed class Unknown(ushort kind, byte[] body) : EventFrame
    {
        public byte[] Body { get; } = body;

        public override ushort Kind { get; } = kind;
    }

    /// <summary>Encode the frame (header + body) into a byte array.</summary>
    /// <exception cref="EventFrameException">The frame body exceeds <see cref="EventChannelProtocol.MaxFrameBodyBytes"/>.</exception>
    public byte[] Encode()
    {
        var body = this switch
        {
            Hello hello => EncodeHelloBody(hello),
            StatusUpdated or Finish => [],
            Stdout stdout => Encoding.UTF8.GetBytes(stdout.Data),
            Stderr stderr => Encoding.UTF8.GetBytes(stderr.Data),
            StdoutOverflow overflow => EncodeOverflowBody(overflow.BytesSkipped),
            StderrOverflow overflow => EncodeOverflowBody(overflow.BytesSkipped),
            Unknown unknown => unknown.Body,
            _ => throw new EventFrameException($"Unsupported frame type {GetType().Name}."),
        };

        if (body.Length > EventChannelProtocol.MaxFrameBodyBytes)
        {
            throw new EventFrameException(
                $"Frame body size {body.Length} exceeds the maximum of {EventChannelProtocol.MaxFrameBodyBytes} bytes.");
        }

        var frame = new byte[EventChannelProtocol.FrameHeaderSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)body.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), Kind);
        body.CopyTo(frame.AsSpan(EventChannelProtocol.FrameHeaderSize));
        return frame;
    }

    /// <summary>Decode a frame from a kind discriminator and body bytes.</summary>
    /// <exception cref="EventFrameException">The body length or content is invalid for the frame kind.</exception>
    public static EventFrame DecodeBody(ushort kind, ReadOnlySpan<byte> body)
    {
        switch (kind)
        {
            case (ushort)EventFrameKind.Hello:
                ExpectLength(kind, body, 4);
                return new Hello(
                    BinaryPrimitives.ReadUInt16LittleEndian(body),
                    BinaryPrimitives.ReadUInt16LittleEndian(body[2..]));

            case (ushort)EventFrameKind.StatusUpdated:
                ExpectLength(kind, body, 0);
                return new StatusUpdated();

            case (ushort)EventFrameKind.Stdout:
                return new Stdout(DecodeUtf8(body));

            case (ushort)EventFrameKind.Stderr:
                return new Stderr(DecodeUtf8(body));

            case (ushort)EventFrameKind.Finish:
                ExpectLength(kind, body, 0);
                return new Finish();

            case (ushort)EventFrameKind.StdoutOverflow:
                ExpectLength(kind, body, 4);
                return new StdoutOverflow(BinaryPrimitives.ReadUInt32LittleEndian(body));

            case (ushort)EventFrameKind.StderrOverflow:
                ExpectLength(kind, body, 4);
                return new StderrOverflow(BinaryPrimitives.ReadUInt32LittleEndian(body));

            default:
                return new Unknown(kind, body.ToArray());
        }
    }

    private static byte[] EncodeHelloBody(Hello hello)
    {
        var body = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(body, hello.VersionMajor);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), hello.VersionMinor);
        return body;
    }

    private static byte[] EncodeOverflowBody(uint bytesSkipped)
    {
        var body = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(body, bytesSkipped);
        return body;
    }

    private static void ExpectLength(ushort kind, ReadOnlySpan<byte> body, int expected)
    {
        if (body.Length != expected)
        {
            throw new EventFrameException(
                $"Frame kind 0x{kind:x4} expects a body of {expected} bytes, got {body.Length}.");
        }
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> body)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(body);
        }
        catch (DecoderFallbackException e)
        {
            throw new EventFrameException("Stdout/stderr frame body is not valid UTF-8.", e);
        }
    }
}

/// <summary>Error produced while encoding or decoding event frames.</summary>
public sealed class EventFrameException : Exception
{
    public EventFrameException(string message)
        : base(message)
    {
    }

    public EventFrameException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Incremental decoder for a <c>NOW_BROKER</c> event frame stream. Feed raw bytes read
/// from the channel with <see cref="Extend"/>, then drain complete frames with
/// <see cref="TryReadFrame"/>.
/// </summary>
public sealed class EventFrameDecoder
{
    private readonly MemoryStream _buffer = new();

    /// <summary>
    /// True when the decoder holds buffered bytes that do not yet form a complete frame.
    /// If the transport reaches end-of-stream while this is true, the frame stream was
    /// truncated mid-frame.
    /// </summary>
    public bool HasBufferedData => _buffer.Length > 0;

    /// <summary>Append raw bytes received from the transport.</summary>
    public void Extend(ReadOnlySpan<byte> bytes)
    {
        _buffer.Write(bytes);
    }

    /// <summary>
    /// Try to decode the next complete frame. Returns false when more bytes are needed.
    /// Frames with unknown kinds are returned as <see cref="EventFrame.Unknown"/> and
    /// should be ignored by the consumer.
    /// </summary>
    /// <exception cref="EventFrameException">
    /// The stream is corrupt (oversized frame, malformed body, or invalid UTF-8); the
    /// channel should be closed.
    /// </exception>
    public bool TryReadFrame(out EventFrame? frame)
    {
        frame = null;
        var buffered = _buffer.GetBuffer().AsSpan(0, (int)_buffer.Length);
        if (buffered.Length < EventChannelProtocol.FrameHeaderSize)
        {
            return false;
        }

        var size = BinaryPrimitives.ReadUInt32LittleEndian(buffered);
        if (size > EventChannelProtocol.MaxFrameBodyBytes)
        {
            throw new EventFrameException(
                $"Frame body size {size} exceeds the maximum of {EventChannelProtocol.MaxFrameBodyBytes} bytes.");
        }

        var total = EventChannelProtocol.FrameHeaderSize + (int)size;
        if (buffered.Length < total)
        {
            return false;
        }

        var kind = BinaryPrimitives.ReadUInt16LittleEndian(buffered[4..]);
        frame = EventFrame.DecodeBody(kind, buffered[EventChannelProtocol.FrameHeaderSize..total]);

        var remainder = buffered[total..].ToArray();
        _buffer.SetLength(0);
        _buffer.Write(remainder);
        return true;
    }
}