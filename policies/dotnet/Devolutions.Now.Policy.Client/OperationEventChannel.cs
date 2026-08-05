using System.IO.Pipes;
using System.Runtime.CompilerServices;

using Devolutions.Now.Policy.Api;

namespace Devolutions.Now.Policy.Client;

/// <summary>
/// Client side of a per-operation event channel carrying the <c>NOW_BROKER</c> frame
/// protocol. Obtain an instance with
/// <see cref="BrokerClient.OpenEventChannel(ExecutionResponse, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// The channel is one-way: the broker pushes stdout/stderr data and status change
/// notifications, the client only reads. The first frame is always
/// <see cref="EventFrame.Hello"/>; after <see cref="EventFrame.Finish"/> the channel can
/// be disposed. See <c>policies/docs/event-channel-protocol.md</c>.
/// </remarks>
public sealed class OperationEventChannel : IAsyncDisposable, IDisposable
{
    private const int ConnectTimeoutMs = 5000;
    private const int ReadBufferSize = 8192;

    private readonly Stream _stream;
    private readonly EventFrameDecoder _decoder = new();
    private readonly byte[] _readBuffer = new byte[ReadBufferSize];
    private bool _helloReceived;

    internal OperationEventChannel(Stream stream)
    {
        _stream = stream;
    }

    internal static async Task<OperationEventChannel> ConnectLocalPipe(string path, CancellationToken cancellationToken)
    {
        var pipeName = NormalizePipeName(path);
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(ConnectTimeoutMs);
            await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);
            return new OperationEventChannel(pipe);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new BrokerClientException(
                BrokerClientErrorKind.Timeout,
                $"Timed out connecting to the operation event channel pipe '{pipeName}'.",
                innerException: ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new BrokerClientException(
                BrokerClientErrorKind.BrokerUnavailable,
                $"Unable to connect to the operation event channel pipe '{pipeName}': {ex.Message}",
                innerException: ex);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Read the next frame from the channel. Returns <c>null</c> when the broker closed
    /// the channel. Frames with unknown kinds are returned as
    /// <see cref="EventFrame.Unknown"/> and should be ignored by the caller.
    /// </summary>
    /// <exception cref="EventFrameException">
    /// The frame stream is corrupt, the channel was closed mid-frame, the stream does
    /// not start with a <c>Hello</c> frame, or the advertised protocol major version is
    /// unsupported; the channel should be disposed.
    /// </exception>
    /// <exception cref="IOException">The transport failed.</exception>
    public async Task<EventFrame?> ReadFrame(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_decoder.TryReadFrame(out var frame))
            {
                EnforceHandshake(frame!);
                return frame;
            }

            var bytesRead = await _stream.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (_decoder.HasBufferedData)
                {
                    throw new EventFrameException("The event channel was closed mid-frame; the frame stream is truncated.");
                }

                return null;
            }

            _decoder.Extend(_readBuffer.AsSpan(0, bytesRead));
        }
    }

    private void EnforceHandshake(EventFrame frame)
    {
        if (_helloReceived)
        {
            return;
        }

        if (frame is not EventFrame.Hello hello)
        {
            throw new EventFrameException(
                $"The event channel did not start with a Hello frame (got kind 0x{frame.Kind:x4}).");
        }

        if (hello.VersionMajor != EventChannelProtocol.VersionMajor)
        {
            throw new EventFrameException(
                $"Unsupported event channel protocol major version {hello.VersionMajor}; "
                + $"this client supports version {EventChannelProtocol.VersionMajor}.");
        }

        _helloReceived = true;
    }

    /// <summary>
    /// Asynchronously enumerate operation events. Frames with unknown kinds are skipped;
    /// the sequence completes after <see cref="EventFrame.Finish"/> or when the broker
    /// closes the channel.
    /// </summary>
    /// <exception cref="EventFrameException">The frame stream is corrupt; the channel should be disposed.</exception>
    /// <exception cref="IOException">The transport failed.</exception>
    public async IAsyncEnumerable<EventFrame> ReadEvents([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var frame = await ReadFrame(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                yield break;
            }

            if (frame is EventFrame.Unknown)
            {
                continue;
            }

            yield return frame;

            if (frame is EventFrame.Finish)
            {
                yield break;
            }
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return _stream.DisposeAsync();
    }

    private static string NormalizePipeName(string path)
    {
        const string win32PipePrefix = @"\\.\pipe\";

        return path.StartsWith(win32PipePrefix, StringComparison.OrdinalIgnoreCase)
            ? path[win32PipePrefix.Length..]
            : path;
    }
}