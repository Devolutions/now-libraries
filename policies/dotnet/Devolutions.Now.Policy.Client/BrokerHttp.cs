using System.Globalization;
using System.Text;

namespace Devolutions.Now.Policy.Client;

/// <summary>Response read by <see cref="BrokerHttp"/>, with the headers needed by the transport.</summary>
internal sealed record BrokerHttpResponse(int StatusCode, string Body, string? RetryAfter);

/// <summary>The broker closed the connection without sending any response byte.</summary>
/// <param name="requestSent">
/// Whether the complete request may have reached the broker. When <c>false</c>, the broker cannot have processed it.
/// </param>
internal sealed class BrokerConnectionClosedException(bool requestSent, Exception? innerException = null)
    : Exception("The package broker closed the connection without responding.", innerException)
{
    public bool RequestSent { get; } = requestSent;
}

/// <summary>Strict HTTP/1.1 framing for the single request/response exchange of a broker pipe connection.</summary>
internal static class BrokerHttp
{
    private static ReadOnlySpan<byte> HeaderTerminator => "\r\n\r\n"u8;

    internal static byte[] EncodeRequest(BrokerTransportRequest request)
    {
        var builder = new StringBuilder()
            .Append(request.Method).Append(' ').Append(request.Path).Append(" HTTP/1.1\r\n")
            .Append("Host: now-package-broker\r\n")
            .Append("Connection: close\r\n");

        foreach (var (name, value) in request.Headers)
        {
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        var bodyByteCount = request.Body is null ? 0 : Encoding.UTF8.GetByteCount(request.Body);
        builder.Append("Content-Length: ").Append(bodyByteCount.ToString(CultureInfo.InvariantCulture)).Append("\r\n\r\n");

        var header = builder.ToString();
        var buffer = new byte[Encoding.ASCII.GetByteCount(header) + bodyByteCount];
        var written = Encoding.ASCII.GetBytes(header, buffer);
        if (request.Body is not null)
        {
            Encoding.UTF8.GetBytes(request.Body, buffer.AsSpan(written));
        }

        return buffer;
    }

    /// <summary>Write the complete request, header and body, in a single write.</summary>
    /// <exception cref="BrokerConnectionClosedException">The broker closed the connection during the write.</exception>
    internal static async Task WriteRequest(Stream stream, BrokerTransportRequest request, CancellationToken cancellationToken)
    {
        var bytes = EncodeRequest(request);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            // A failed write means the broker did not read the complete request, so it cannot have processed it.
            throw new BrokerConnectionClosedException(requestSent: false, ex);
        }

        try
        {
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new BrokerConnectionClosedException(requestSent: true, ex);
        }
    }

    /// <exception cref="BrokerConnectionClosedException">The broker closed the connection before sending any response byte.</exception>
    internal static async Task<BrokerHttpResponse> ReadResponse(
        Stream stream,
        string path,
        int maxHeaderBytes,
        int maxBodyBytes,
        BrokerClientErrorKind incompleteResponseKind,
        CancellationToken cancellationToken)
    {
        var headerBuffer = new byte[maxHeaderBytes];
        var totalRead = 0;
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            if (totalRead == headerBuffer.Length)
            {
                throw Failure(BrokerClientErrorKind.InvalidResponse, $"The package broker returned response headers that are too large for {path}.", path);
            }

            int read;
            try
            {
                read = await stream.ReadAsync(headerBuffer.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex) when (totalRead == 0)
            {
                throw new BrokerConnectionClosedException(requestSent: true, ex);
            }

            if (read == 0)
            {
                if (totalRead == 0)
                {
                    throw new BrokerConnectionClosedException(requestSent: true);
                }

                throw Failure(incompleteResponseKind, $"The package broker closed the connection before sending a complete response for {path}.", path);
            }

            var searchStart = Math.Max(0, totalRead - (HeaderTerminator.Length - 1));
            totalRead += read;
            var index = headerBuffer.AsSpan(searchStart, totalRead - searchStart).IndexOf(HeaderTerminator);
            if (index >= 0)
            {
                headerEnd = searchStart + index;
            }
        }

        var headerText = Encoding.ASCII.GetString(headerBuffer, 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var statusCode = ParseStatusLine(lines[0], path);

        int? contentLength = null;
        string? retryAfter = null;
        for (var i = 1; i < lines.Length; i++)
        {
            var separator = lines[i].IndexOf(':');
            if (separator <= 0 || !IsToken(lines[i].AsSpan(0, separator)))
            {
                throw Failure(BrokerClientErrorKind.InvalidResponse, $"The package broker returned a malformed response header for {path}.", path, statusCode);
            }

            var name = lines[i][..separator];
            var value = lines[i][(separator + 1)..].Trim(' ', '\t');

            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (contentLength is not null
                    || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                {
                    throw Failure(BrokerClientErrorKind.InvalidResponse, $"The package broker returned an invalid Content-Length for {path}.", path, statusCode);
                }

                if (parsed > maxBodyBytes)
                {
                    throw Failure(BrokerClientErrorKind.InvalidResponse, $"The package broker response for {path} exceeds the {maxBodyBytes}-byte response size limit.", path, statusCode);
                }

                contentLength = parsed;
            }
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                throw Failure(BrokerClientErrorKind.InvalidResponse, $"The package broker returned an unsupported Transfer-Encoding for {path}.", path, statusCode);
            }
            else if (name.Equals("Retry-After", StringComparison.OrdinalIgnoreCase))
            {
                retryAfter = value;
            }
        }

        if (contentLength is null)
        {
            throw Failure(BrokerClientErrorKind.InvalidResponse, $"The package broker response for {path} omitted Content-Length.", path, statusCode);
        }

        var bodyLength = contentLength.Value;
        var bodyStart = headerEnd + HeaderTerminator.Length;
        var alreadyRead = totalRead - bodyStart;
        if (alreadyRead > bodyLength)
        {
            throw Failure(BrokerClientErrorKind.InvalidResponse, $"The package broker returned more response data than declared for {path}.", path, statusCode);
        }

        var body = new byte[bodyLength];
        headerBuffer.AsSpan(bodyStart, alreadyRead).CopyTo(body);
        var bodyRead = alreadyRead;
        while (bodyRead < bodyLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(bodyRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw Failure(incompleteResponseKind, $"The package broker closed the connection before sending the complete response body for {path}.", path);
            }

            bodyRead += read;
        }

        // Requests send Connection: close, so the response ends with the connection. Probe one byte past
        // Content-Length so that excess data is rejected regardless of how the reads were chunked.
        var trailing = new byte[1];
        if (await stream.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw Failure(BrokerClientErrorKind.InvalidResponse, $"The package broker returned more response data than declared for {path}.", path, statusCode);
        }

        return new BrokerHttpResponse(statusCode, Encoding.UTF8.GetString(body), retryAfter);
    }

    private static int ParseStatusLine(string statusLine, string path)
    {
        // The broker always answers with "HTTP/1.1 <code> <reason>"; the reason phrase may be empty.
        var parts = statusLine.Split(' ', 3);
        if (parts.Length != 3
            || parts[0] != "HTTP/1.1"
            || parts[1].Length != 3
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var statusCode)
            || statusCode is < 100 or > 599)
        {
            throw Failure(BrokerClientErrorKind.InvalidResponse, $"The package broker returned an invalid HTTP status line for {path}.", path);
        }

        return statusCode;
    }

    /// <summary>Whether <paramref name="value"/> is an HTTP token (RFC 9110 <c>tchar</c>), as required for field names.</summary>
    private static bool IsToken(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && !"!#$%&'*+-.^_`|~".Contains(c))
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    private static BrokerClientException Failure(BrokerClientErrorKind kind, string message, string path, int? statusCode = null) =>
        new(kind, message, path, statusCode);
}