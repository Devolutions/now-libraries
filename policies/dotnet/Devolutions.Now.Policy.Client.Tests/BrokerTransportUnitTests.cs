using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

using Devolutions.Now.Policy.Client;

using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

public class BrokerTransportUnitTests
{
    private const string Path = "/v1/health";

    [Fact]
    public void Pipe_access_is_generic_read_and_write_data_only()
    {
        const int FileGenericRead = 0x00120089;
        const int FileWriteData = 0x00000002;

        var access = NamedPipeBrokerTransport.BrokerPipeAccessRights;

        Assert.Equal(FileGenericRead | FileWriteData, (int)access);
        Assert.Equal((PipeAccessRights)0, access & PipeAccessRights.CreateNewInstance);
        Assert.Equal((PipeAccessRights)0, access & PipeAccessRights.WriteAttributes);
        Assert.Equal((PipeAccessRights)0, access & PipeAccessRights.WriteExtendedAttributes);
        Assert.Equal((PipeAccessRights)0, access & PipeAccessRights.ChangePermissions);
        Assert.Equal((PipeAccessRights)0, access & PipeAccessRights.TakeOwnership);
        Assert.Equal((PipeAccessRights)0, access & PipeAccessRights.Delete);
    }

    [Fact]
    public void Options_default_to_hardened_settings()
    {
        var options = new NamedPipeBrokerTransportOptions();

        Assert.Null(options.PipeName);
        Assert.Equal(BrokerApi.DefaultPipeName, options.ResolvedPipeName);
        Assert.Equal(TokenImpersonationLevel.Identification, options.ImpersonationLevel);
        Assert.True(options.VerifyServer);
        Assert.Equal(["DevolutionsAgent", "devolutions-agent"], options.ServerServiceNames);
        Assert.Null(options.ServerAuthenticator);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ResponseTimeout);
        Assert.Equal(64 * 1024, options.MaxResponseHeaderBytes);
        Assert.Equal(4 * BrokerApi.MaxPolicyManagementBodyBytes, options.MaxResponseBodyBytes);
        Assert.Equal(3, options.MaxBusyRetries);
        Assert.Equal(TimeSpan.FromSeconds(5), options.MaxBusyRetryDelay);
        Assert.Equal(BrokerClientErrorKind.InvalidResponse, options.IncompleteResponseErrorKind);
    }

    public static TheoryData<NamedPipeBrokerTransportOptions> InvalidOptions => new()
    {
        new NamedPipeBrokerTransportOptions { ConnectTimeout = TimeSpan.Zero },
        new NamedPipeBrokerTransportOptions { ResponseTimeout = TimeSpan.FromSeconds(-1) },
        new NamedPipeBrokerTransportOptions { ServerAuthenticatorTimeout = TimeSpan.Zero },
        new NamedPipeBrokerTransportOptions { MaxResponseBodyBytes = -1 },
        new NamedPipeBrokerTransportOptions { MaxResponseHeaderBytes = 0 },
        new NamedPipeBrokerTransportOptions { MaxBusyRetries = -1 },
        new NamedPipeBrokerTransportOptions { MaxBusyRetryDelay = TimeSpan.FromSeconds(-1) },
        new NamedPipeBrokerTransportOptions { ImpersonationLevel = (TokenImpersonationLevel)42 },
        new NamedPipeBrokerTransportOptions { IncompleteResponseErrorKind = (BrokerClientErrorKind)42 },
        new NamedPipeBrokerTransportOptions { ServerServiceNames = ["DevolutionsAgent", " "] },
        new NamedPipeBrokerTransportOptions { ServerServiceNames = null! },
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Transport_rejects_invalid_options(NamedPipeBrokerTransportOptions options)
    {
        Assert.ThrowsAny<ArgumentException>(() => new NamedPipeBrokerTransport(options));
    }

    [Fact]
    public void Request_is_encoded_as_one_buffer_with_framing_headers_owned_by_the_transport()
    {
        var bytes = BrokerHttp.EncodeRequest(new BrokerTransportRequest
        {
            Method = "POST",
            Path = "/v1/package-operations/evaluate",
            Body = "{\"é\":1}",
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Host"] = "other",
                ["Connection"] = "keep-alive",
                ["Content-Length"] = "999",
            },
        });

        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal(
            "POST /v1/package-operations/evaluate HTTP/1.1\r\n"
            + "Host: now-package-broker\r\n"
            + "Connection: close\r\n"
            + "Content-Type: application/json\r\n"
            + "Content-Length: 8\r\n\r\n"
            + "{\"é\":1}",
            text);
    }

    [Fact]
    public async Task Response_is_parsed_with_retry_after()
    {
        var response = await Read("HTTP/1.1 503 Service Unavailable\r\ncontent-type: application/json\r\nretry-after: 1\r\ncontent-length: 2\r\n\r\n{}");

        Assert.Equal(503, response.StatusCode);
        Assert.Equal("{}", response.Body);
        Assert.Equal("1", response.RetryAfter);
    }

    [Fact]
    public async Task Response_body_may_be_empty()
    {
        var response = await Read("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n");

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("", response.Body);
        Assert.Null(response.RetryAfter);
    }

    [Theory]
    [InlineData("HTTP/1.1 200 OK\r\n\r\n{}", "omitted Content-Length")]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nContent-Length: 2\r\n\r\n{}", "invalid Content-Length")]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Length: +2\r\n\r\n{}", "invalid Content-Length")]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Length: -1\r\n\r\n", "invalid Content-Length")]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Length: 99999999999\r\n\r\n", "invalid Content-Length")]
    [InlineData("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nContent-Length: 2\r\n\r\n{}", "Transfer-Encoding")]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Length: 1\r\n\r\n{}", "more response data")]
    [InlineData("HTTP/1.1 200 OK\r\nbroken header\r\nContent-Length: 2\r\n\r\n{}", "malformed response header")]
    public async Task Malformed_framing_is_rejected_with_the_status_code(string raw, string message)
    {
        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => Read(raw));

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, ex.Kind);
        Assert.Equal(200, ex.StatusCode);
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public async Task Excess_data_after_the_body_is_rejected_even_when_read_separately()
    {
        var stream = new ChunkedStream(
            Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\n"),
            Encoding.ASCII.GetBytes("{}"),
            Encoding.ASCII.GetBytes("x"));

        var ex = await Assert.ThrowsAsync<BrokerClientException>(
            () => BrokerHttp.ReadResponse(stream, Path, 1024, 1024, BrokerClientErrorKind.InvalidResponse, default));

        Assert.Contains("more response data", ex.Message);
    }

    [Theory]
    [InlineData("HTTP/1.1 200 OK\r\nCont")]
    [InlineData("")]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\n{}")]
    public async Task Incomplete_responses_use_the_configured_error_kind(string raw)
    {
        var ex = await Assert.ThrowsAsync<BrokerClientException>(
            () => Read(raw, incompleteResponseKind: BrokerClientErrorKind.BrokerUnavailable));

        Assert.Equal(BrokerClientErrorKind.BrokerUnavailable, ex.Kind);
        Assert.Null(ex.StatusCode);
        Assert.Contains("closed the connection", ex.Message);
    }

    [Theory]
    [InlineData("HTTP/1.1 2000 OK\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("HTTP/1.1 +20 OK\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("SSH-2.0 200 OK\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("HTTP/1.x 200 OK\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("HTTP/1.1garbage 200 OK\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("garbage\r\n\r\n")]
    public async Task Invalid_status_lines_are_rejected(string raw)
    {
        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => Read(raw));

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, ex.Kind);
        Assert.Contains("status line", ex.Message);
    }

    [Fact]
    public async Task Oversized_body_is_rejected_before_reading_it()
    {
        var stream = new ChunkedStream(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1025\r\n\r\n"));

        var ex = await Assert.ThrowsAsync<BrokerClientException>(
            () => BrokerHttp.ReadResponse(stream, Path, 1024, 1024, BrokerClientErrorKind.InvalidResponse, default));

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, ex.Kind);
        Assert.Equal(200, ex.StatusCode);
        Assert.Contains("1024-byte response size limit", ex.Message);
        Assert.Equal(1, stream.ReadCount);
    }

    [Fact]
    public async Task Oversized_headers_are_rejected()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Padding: " + new string('a', 200) + "\r\nContent-Length: 0\r\n\r\n";

        var ex = await Assert.ThrowsAsync<BrokerClientException>(
            () => BrokerHttp.ReadResponse(new ChunkedStream(Encoding.ASCII.GetBytes(raw)), Path, 128, 1024, BrokerClientErrorKind.InvalidResponse, default));

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, ex.Kind);
        Assert.Contains("too large", ex.Message);
    }

    [Fact]
    public async Task Header_terminator_split_across_reads_is_found()
    {
        var stream = new ChunkedStream(
            Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r"),
            Encoding.ASCII.GetBytes("\n{"),
            Encoding.ASCII.GetBytes("}"));

        var response = await BrokerHttp.ReadResponse(stream, Path, 1024, 1024, BrokerClientErrorKind.InvalidResponse, default);

        Assert.Equal("{}", response.Body);
    }

    [Theory]
    [InlineData(503, "1", 1000)]
    [InlineData(503, "0", 0)]
    [InlineData(503, "3600", 5000)]
    [InlineData(503, "99999999999999999999", null)]
    [InlineData(503, null, null)]
    [InlineData(503, "soon", null)]
    [InlineData(503, "-1", null)]
    [InlineData(500, "1", null)]
    [InlineData(429, "1", null)]
    [InlineData(200, "1", null)]
    public void Busy_retry_requires_503_with_retry_after_and_caps_the_delay(int statusCode, string? retryAfter, int? expectedMs)
    {
        var delay = BrokerBusyRetry.GetRetryDelay(
            new BrokerHttpResponse(statusCode, "", retryAfter),
            TimeSpan.FromSeconds(5),
            DateTimeOffset.UnixEpoch);

        Assert.Equal(expectedMs is null ? null : TimeSpan.FromMilliseconds(expectedMs.Value), delay);
    }

    [Fact]
    public void Busy_retry_honors_http_date_retry_after()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            TimeSpan.FromSeconds(2),
            BrokerBusyRetry.GetRetryDelay(new BrokerHttpResponse(503, "", "Fri, 02 Oct 2026 12:00:02 GMT"), TimeSpan.FromSeconds(5), now));
        Assert.Equal(
            TimeSpan.Zero,
            BrokerBusyRetry.GetRetryDelay(new BrokerHttpResponse(503, "", "Fri, 02 Oct 2026 11:00:00 GMT"), TimeSpan.FromSeconds(5), now));
    }

    [Fact]
    public void Verifier_accepts_the_running_agent_service_process()
    {
        var inspector = new FakeInspector { ["DevolutionsAgent"] = new(true, 1234, "LocalSystem") };

        BrokerServerVerifier.Verify(1234, ["DevolutionsAgent", "devolutions-agent"], inspector, Path);
    }

    [Fact]
    public void Verifier_falls_back_to_the_manually_registered_service()
    {
        var inspector = new FakeInspector { ["devolutions-agent"] = new(true, 1234, @"NT AUTHORITY\SYSTEM") };

        BrokerServerVerifier.Verify(1234, ["DevolutionsAgent", "devolutions-agent"], inspector, Path);
    }

    [Fact]
    public void Verifier_accepts_a_readable_local_system_token()
    {
        var inspector = new FakeInspector { UserSid = "S-1-5-18", ["DevolutionsAgent"] = new(true, 1234, ".\\LocalSystem") };

        BrokerServerVerifier.Verify(1234, ["DevolutionsAgent"], inspector, Path);
    }

    [Theory]
    [InlineData(0, "could not be determined")]
    [InlineData(-1, "could not be determined")]
    public void Verifier_rejects_unknown_server_process(int serverProcessId, string message)
    {
        var inspector = new FakeInspector { ["DevolutionsAgent"] = new(true, serverProcessId, "LocalSystem") };

        AssertVerificationFails(() => BrokerServerVerifier.Verify(serverProcessId, ["DevolutionsAgent"], inspector, Path), message);
    }

    [Fact]
    public void Verifier_rejects_a_server_that_is_not_the_service_process()
    {
        var inspector = new FakeInspector { ["DevolutionsAgent"] = new(true, 1234, "LocalSystem") };

        AssertVerificationFails(
            () => BrokerServerVerifier.Verify(4321, ["DevolutionsAgent", "devolutions-agent"], inspector, Path),
            "is not the running 'DevolutionsAgent' or 'devolutions-agent' service process");
    }

    [Fact]
    public void Verifier_rejects_a_stopped_service()
    {
        var inspector = new FakeInspector { ["DevolutionsAgent"] = new(false, 1234, "LocalSystem") };

        AssertVerificationFails(() => BrokerServerVerifier.Verify(1234, ["DevolutionsAgent"], inspector, Path), "is not the running");
    }

    [Fact]
    public void Verifier_rejects_when_no_service_is_installed()
    {
        AssertVerificationFails(
            () => BrokerServerVerifier.Verify(1234, ["DevolutionsAgent", "devolutions-agent"], new FakeInspector(), Path),
            "no 'DevolutionsAgent' or 'devolutions-agent' service is installed");
    }

    [Theory]
    [InlineData("NT AUTHORITY\\LocalService")]
    [InlineData(".\\someone")]
    [InlineData("")]
    [InlineData(null)]
    public void Verifier_rejects_a_service_not_running_as_local_system(string? accountName)
    {
        var inspector = new FakeInspector { ["DevolutionsAgent"] = new(true, 1234, accountName) };

        AssertVerificationFails(() => BrokerServerVerifier.Verify(1234, ["DevolutionsAgent"], inspector, Path), "not configured to run as LocalSystem");
    }

    [Fact]
    public void Verifier_rejects_a_readable_token_of_another_user()
    {
        var inspector = new FakeInspector { UserSid = "S-1-5-21-1-2-3-1001", ["DevolutionsAgent"] = new(true, 1234, "LocalSystem") };

        AssertVerificationFails(() => BrokerServerVerifier.Verify(1234, ["DevolutionsAgent"], inspector, Path), "does not run as LocalSystem");
    }

    [Fact]
    public void Verifier_without_service_names_requires_a_local_system_token()
    {
        BrokerServerVerifier.Verify(1234, [], new FakeInspector { UserSid = "S-1-5-18" }, Path);

        AssertVerificationFails(() => BrokerServerVerifier.Verify(1234, [], new FakeInspector(), Path), "account could not be read");
        AssertVerificationFails(
            () => BrokerServerVerifier.Verify(1234, [], new FakeInspector { UserSid = "S-1-5-21-1-2-3-1001" }, Path),
            "does not run as LocalSystem");
    }

    private static void AssertVerificationFails(Action verify, string message)
    {
        var ex = Assert.Throws<BrokerClientException>(verify);
        Assert.Equal(BrokerClientErrorKind.ServerVerificationFailed, ex.Kind);
        Assert.Equal(Path, ex.Endpoint);
        Assert.Contains(message, ex.Message);
    }

    private static Task<BrokerHttpResponse> Read(
        string raw,
        BrokerClientErrorKind incompleteResponseKind = BrokerClientErrorKind.InvalidResponse) =>
        BrokerHttp.ReadResponse(
            new MemoryStream(Encoding.UTF8.GetBytes(raw)),
            Path,
            NamedPipeBrokerTransportOptions.DefaultMaxResponseHeaderBytes,
            1024,
            incompleteResponseKind,
            default);

    private sealed class FakeInspector : Dictionary<string, BrokerServiceInfo>, IBrokerServerInspector
    {
        public string? UserSid { get; init; }

        public BrokerServiceInfo? QueryService(string serviceName) => TryGetValue(serviceName, out var info) ? info : null;

        public string? TryGetServerProcessUserSid() => UserSid;
    }

    /// <summary>Read-only stream returning one chunk per read.</summary>
    private sealed class ChunkedStream(params byte[][] chunks) : Stream
    {
        private readonly Queue<byte[]> _chunks = new(chunks);

        public int ReadCount { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            ReadCount++;
            if (!_chunks.TryPeek(out var chunk))
            {
                return 0;
            }

            var count = Math.Min(chunk.Length, buffer.Length);
            chunk.AsSpan(0, count).CopyTo(buffer);
            _chunks.Dequeue();
            if (count < chunk.Length)
            {
                // Keep the unread remainder at the head of the queue.
                var remaining = new Queue<byte[]>();
                remaining.Enqueue(chunk[count..]);
                while (_chunks.TryDequeue(out var next))
                {
                    remaining.Enqueue(next);
                }

                while (remaining.TryDequeue(out var next))
                {
                    _chunks.Enqueue(next);
                }
            }

            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer.AsSpan(offset, count)));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}