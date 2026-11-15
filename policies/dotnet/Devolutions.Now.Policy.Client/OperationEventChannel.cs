using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.Principal;

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
    private readonly bool _completeCapture;
    private bool _helloReceived;
    private bool _finishReceived;
    private bool _overflowReceived;
    private bool _faulted;

    internal OperationEventChannel(Stream stream, bool completeCapture = false)
    {
        _stream = stream;
        _completeCapture = completeCapture;
    }

    /// <summary>
    /// <c>true</c> only after a valid <c>Hello</c> and a <c>Finish</c> were read, with no
    /// stdout or stderr overflow frame and no malformed frame seen.
    /// This does not detect complete frames consumed by another reader, or output written or
    /// held open by other processes that inherited the command's output handles.
    /// </summary>
    /// <remarks>
    /// Treat <c>!IsCaptureComplete</c>, or any exception from this channel, as an unknown outcome.
    /// A <c>true</c> value alone does not prove an outcome that depends on the captured output.
    /// Data arriving after the caller stops reading cannot be detected.
    /// </remarks>
    public bool IsCaptureComplete => _helloReceived && _finishReceived && !_overflowReceived && !_faulted;

    internal static Task<OperationEventChannel> ConnectLocalPipe(string path, CancellationToken cancellationToken)
        => Connect(path, expectedServer: null, authenticator: null, cancellationToken);

    internal static Task<OperationEventChannel> ConnectGrantPipe(
        string path,
        BrokerPipeServerIdentity expectedServer,
        IBrokerServerAuthenticator authenticator,
        CancellationToken cancellationToken)
        => Connect(path, expectedServer, authenticator, cancellationToken);

    private static async Task<OperationEventChannel> Connect(string path,
        BrokerPipeServerIdentity? expectedServer,
        IBrokerServerAuthenticator? authenticator,
        CancellationToken cancellationToken)
    {
        var pipeName = NormalizePipeName(path);
        var pipe = expectedServer is null
            ? new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous)
            : new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.In,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(ConnectTimeoutMs);
            await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);
            if (expectedServer is { } expected)
            {
                AuthenticateSameServer(pipe, expected, authenticator!);
            }

            return new OperationEventChannel(pipe, completeCapture: expectedServer is not null);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new BrokerClientException(
                BrokerClientErrorKind.Timeout,
                $"Timed out connecting to the operation event channel pipe '{pipeName}'.",
                innerException: ex);
        }
        catch (BrokerClientException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
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

    private static void AuthenticateSameServer(
        NamedPipeClientStream pipe,
        BrokerPipeServerIdentity expected,
        IBrokerServerAuthenticator authenticator)
    {
        try
        {
            var server = PipeServerIdentityReader.Read(pipe);
            if (server != expected)
            {
                throw new InvalidOperationException("The event pipe server is not the broker instance that accepted the operation.");
            }

            authenticator.Authenticate(server);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.ServerAuthenticationFailed,
                "The operation event pipe server could not be authenticated as the broker instance that accepted the operation.",
                innerException: ex);
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
    /// unsupported; the channel should be disposed. A channel opened by
    /// <see cref="BrokerClient.OpenGrantEventChannel"/> also throws when the broker closes it
    /// before <c>Finish</c>, instead of returning <c>null</c>.
    /// </exception>
    /// <exception cref="IOException">The transport failed.</exception>
    public async Task<EventFrame?> ReadFrame(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadFrameCore(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is EventFrameException or IOException or OperationCanceledException)
        {
            _faulted = true;
            throw;
        }
    }

    private async Task<EventFrame?> ReadFrameCore(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_decoder.TryReadFrame(out var frame))
            {
                EnforceHandshake(frame!);
                if (_completeCapture && frame is EventFrame.Finish && _decoder.HasBufferedData)
                {
                    throw new EventFrameException("The event channel has data after Finish.");
                }
                Track(frame!);
                return frame;
            }

            var bytesRead = await _stream.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (_decoder.HasBufferedData)
                {
                    throw new EventFrameException("The event channel was closed mid-frame; the frame stream is truncated.");
                }

                if (_completeCapture && !_finishReceived)
                {
                    throw new EventFrameException("The event channel was closed before Finish; captured output is incomplete.");
                }

                return null;
            }

            if (_completeCapture && _finishReceived)
            {
                throw new EventFrameException("The event channel has data after Finish.");
            }

            _decoder.Extend(_readBuffer.AsSpan(0, bytesRead));
        }
    }

    private void Track(EventFrame frame)
    {
        switch (frame)
        {
            case EventFrame.Finish:
                _finishReceived = true;
                break;
            case EventFrame.StdoutOverflow or EventFrame.StderrOverflow:
                _overflowReceived = true;
                break;
        }
    }

    private void EnforceHandshake(EventFrame frame)
    {
        if (_helloReceived)
        {
            if (_completeCapture && frame is EventFrame.Hello)
            {
                throw new EventFrameException("The event channel sent more than one Hello.");
            }

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