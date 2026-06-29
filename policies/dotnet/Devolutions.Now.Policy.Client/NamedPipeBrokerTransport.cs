using System.IO.Pipes;
using System.Text;

using Devolutions.Now.Policy.Api;

namespace Devolutions.Now.Policy.Client;

/// <summary>Package broker transport using HTTP/1.1 over a Windows named pipe.</summary>
public sealed class NamedPipeBrokerTransport : IBrokerTransport
{
    private const int ConnectTimeoutMs = 5000;
    private const int ReadTimeoutMs = 30000;

    private readonly string _pipeName;

    public NamedPipeBrokerTransport(string? pipeName = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? BrokerApi.DefaultPipeName : pipeName;
    }

    public Transport Kind => Transport.HttpNamedPipe;

    public async Task<BrokerTransportResponse> Send(BrokerTransportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectCts.CancelAfter(ConnectTimeoutMs);
                await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);
            }

            var requestBuilder = new StringBuilder();
            requestBuilder.Append($"{request.Method} {request.Path} HTTP/1.1\r\n");
            requestBuilder.Append("Host: now-package-broker\r\n");
            requestBuilder.Append("Connection: close\r\n");

            foreach (var (key, value) in request.Headers)
            {
                if (!key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    requestBuilder.Append($"{key}: {value}\r\n");
                }
            }

            byte[]? bodyBytes = request.Body is not null ? Encoding.UTF8.GetBytes(request.Body) : null;
            requestBuilder.Append($"Content-Length: {bodyBytes?.Length ?? 0}\r\n");
            requestBuilder.Append("\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(requestBuilder.ToString());
            await pipe.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
            if (bodyBytes is not null)
            {
                await pipe.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
            }

            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);

            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(ReadTimeoutMs);
            return await ReadHttpResponse(pipe, request.Path, readCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.Timeout,
                $"Timed out communicating with the package broker at {request.Path} over named pipe '{_pipeName}'.",
                request.Path,
                innerException: ex);
        }
        catch (IOException ex)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.BrokerUnavailable,
                $"Unable to communicate with the package broker at {request.Path} over named pipe '{_pipeName}': {ex.Message}",
                request.Path,
                innerException: ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.BrokerUnavailable,
                $"Access to the package broker named pipe '{_pipeName}' was denied while calling {request.Path}.",
                request.Path,
                innerException: ex);
        }
    }

    public void Dispose()
    {
        // No persistent resources to dispose.
    }

    private static async Task<BrokerTransportResponse> ReadHttpResponse(Stream stream, string path, CancellationToken ct)
    {
        var buffer = new byte[65536];
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }
            totalRead += bytesRead;

            var currentText = Encoding.ASCII.GetString(buffer, 0, totalRead);
            var headerEnd = currentText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0)
            {
                continue;
            }

            var headerText = currentText[..headerEnd];
            var bodyStart = headerEnd + 4;

            var lines = headerText.Split("\r\n");
            var statusParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (statusParts.Length < 2 || !int.TryParse(statusParts[1], out var statusCode))
            {
                throw new BrokerClientException(
                    BrokerClientErrorKind.InvalidResponse,
                    $"Broker returned an invalid HTTP status line for {path}: '{lines[0]}'.",
                    path);
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length; i++)
            {
                var colonIdx = lines[i].IndexOf(':');
                if (colonIdx > 0)
                {
                    headers[lines[i][..colonIdx].Trim()] = lines[i][(colonIdx + 1)..].Trim();
                }
            }

            var contentLength = 0;
            if (headers.TryGetValue("Content-Length", out var clStr))
            {
                if (!int.TryParse(clStr, out contentLength) || contentLength < 0)
                {
                    throw new BrokerClientException(
                        BrokerClientErrorKind.InvalidResponse,
                        $"Broker returned an invalid Content-Length header for {path}: '{clStr}'.",
                        path);
                }
            }

            var bodyBytesRead = totalRead - bodyStart;
            while (bodyBytesRead < contentLength)
            {
                if (bodyStart + contentLength > buffer.Length)
                {
                    var newBuffer = new byte[bodyStart + contentLength];
                    Buffer.BlockCopy(buffer, 0, newBuffer, 0, totalRead);
                    buffer = newBuffer;
                }

                var read = await stream.ReadAsync(buffer.AsMemory(bodyStart + bodyBytesRead, contentLength - bodyBytesRead), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new BrokerClientException(
                        BrokerClientErrorKind.InvalidResponse,
                        $"Broker closed the pipe before sending the complete response body for {path}.",
                        path);
                }
                bodyBytesRead += read;
                totalRead += read;
            }

            var bodyText = Encoding.UTF8.GetString(buffer, bodyStart, contentLength);
            return new BrokerTransportResponse { StatusCode = statusCode, Body = bodyText };
        }

        throw new BrokerClientException(
            BrokerClientErrorKind.InvalidResponse,
            $"Broker closed the pipe before sending a complete HTTP response for {path}.",
            path);
    }
}