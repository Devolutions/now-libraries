using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

using Devolutions.Now.Policy.Client;

using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

public class NamedPipeBrokerTransportTests
{
    private const string OkResponse = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}";
    private const string BusyResponse =
        "HTTP/1.1 503 Service Unavailable\r\nRetry-After: 1\r\nContent-Length: 2\r\n\r\n{}";

    private static readonly BrokerTransportRequest HealthRequest = new() { Method = "GET", Path = "/v1/health" };

    [Fact]
    public async Task Sends_request_and_reads_response_over_the_pipe()
    {
        await using var server = TestPipeServer.Start(_ => OkResponse);
        using var transport = CreateTransport(server.PipeName);

        var response = await transport.Send(new BrokerTransportRequest
        {
            Method = "POST",
            Path = "/v1/package-operations/evaluate",
            Body = "{\"a\":1}",
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        });

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("{}", response.Body);
        var request = Assert.Single(await server.Completed);
        Assert.StartsWith("POST /v1/package-operations/evaluate HTTP/1.1\r\n", request);
        Assert.Contains("\r\nConnection: close\r\n", request);
        Assert.EndsWith("Content-Length: 7\r\n\r\n{\"a\":1}", request);
    }

    [Fact]
    public async Task Busy_replies_are_retried_over_new_connections()
    {
        await using var server = TestPipeServer.Start(index => index < 2 ? BusyResponse : OkResponse, connections: 3);
        var delays = new List<TimeSpan>();
        using var transport = CreateTransport(server.PipeName, delays);

        var response = await transport.Send(HealthRequest);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], delays);
        Assert.Equal(3, (await server.Completed).Count);
    }

    [Fact]
    public async Task Busy_retries_are_bounded_and_return_the_last_reply()
    {
        await using var server = TestPipeServer.Start(_ => BusyResponse, connections: 3);
        var delays = new List<TimeSpan>();
        using var transport = CreateTransport(server.PipeName, delays, new NamedPipeBrokerTransportOptions { MaxBusyRetries = 2 });

        var response = await transport.Send(HealthRequest);

        Assert.Equal(503, response.StatusCode);
        Assert.Equal(2, delays.Count);
        Assert.Equal(3, (await server.Completed).Count);
    }

    [Fact]
    public async Task Unavailable_replies_without_retry_after_are_not_retried()
    {
        await using var server = TestPipeServer.Start(_ => "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\n\r\n");
        var delays = new List<TimeSpan>();
        using var transport = CreateTransport(server.PipeName, delays);

        var response = await transport.Send(HealthRequest);

        Assert.Equal(503, response.StatusCode);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Default_server_verification_rejects_a_server_that_is_not_the_agent_service()
    {
        await using var server = TestPipeServer.Start(_ => OkResponse);
        using var transport = new NamedPipeBrokerTransport(server.PipeName);

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => transport.Send(HealthRequest));

        Assert.Equal(BrokerClientErrorKind.ServerVerificationFailed, ex.Kind);
        Assert.Equal(HealthRequest.Path, ex.Endpoint);
        Assert.Equal([""], await server.Completed);
    }

    [Fact]
    public async Task Server_authenticator_runs_before_sending_and_its_result_lives_until_the_response()
    {
        await using var server = TestPipeServer.Start(_ => OkResponse);
        var lease = new Lease();
        BrokerPipeServerContext? seen = null;
        using var transport = CreateTransport(server.PipeName, options: new NamedPipeBrokerTransportOptions
        {
            ServerAuthenticator = (context, _) =>
            {
                seen = context;
                Assert.True(context.Pipe.IsConnected);
                Assert.Equal(0, server.ReceivedBytes);
                return ValueTask.FromResult<IDisposable?>(lease);
            },
        });

        var response = await transport.Send(HealthRequest);

        Assert.Equal(200, response.StatusCode);
        Assert.True(lease.Disposed);
        Assert.NotNull(seen);
        Assert.Equal(server.PipeName, seen.PipeName);
        Assert.Equal(HealthRequest.Path, seen.Endpoint);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Environment.ProcessId, seen.ServerProcessId);
            Assert.NotNull(seen.ServerProcess);
            Assert.True(seen.ServerProcess.IsClosed);
        }
    }

    [Fact]
    public async Task Server_authenticator_rejection_sends_nothing()
    {
        await using var server = TestPipeServer.Start(_ => OkResponse);
        using var transport = CreateTransport(server.PipeName, options: new NamedPipeBrokerTransportOptions
        {
            ServerAuthenticator = (_, _) => throw new InvalidOperationException("untrusted"),
        });

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => transport.Send(HealthRequest));

        Assert.Equal(BrokerClientErrorKind.ServerVerificationFailed, ex.Kind);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal([""], await server.Completed);
    }

    [Fact]
    public async Task Server_authenticator_broker_client_exceptions_are_propagated()
    {
        await using var server = TestPipeServer.Start(_ => OkResponse);
        var expected = new BrokerClientException(BrokerClientErrorKind.BrokerUnavailable, "custom");
        using var transport = CreateTransport(server.PipeName, options: new NamedPipeBrokerTransportOptions
        {
            ServerAuthenticator = (_, _) => ValueTask.FromException<IDisposable?>(expected),
        });

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => transport.Send(HealthRequest));

        Assert.Same(expected, ex);
    }

    [Fact]
    public async Task Server_authenticator_timeout_fails_and_disposes_the_late_result()
    {
        await using var server = TestPipeServer.Start(_ => OkResponse);
        var release = new TaskCompletionSource();
        var lease = new Lease();
        BrokerPipeServerContext? seen = null;
        using var transport = CreateTransport(server.PipeName, options: new NamedPipeBrokerTransportOptions
        {
            ServerAuthenticatorTimeout = TimeSpan.FromMilliseconds(50),
            ServerAuthenticator = async (context, _) =>
            {
                seen = context;
                await release.Task;
                return lease;
            },
        });

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => transport.Send(HealthRequest));

        Assert.Equal(BrokerClientErrorKind.Timeout, ex.Kind);
        Assert.False(lease.Disposed);
        Assert.NotNull(seen);
        Assert.True(seen.Pipe.IsConnected);

        release.SetResult();
        await lease.WaitDisposed();
        Assert.Equal([""], await server.Completed);
    }

    [Fact]
    public async Task Server_authenticator_blocking_synchronously_is_bounded_by_the_timeout()
    {
        await using var server = TestPipeServer.Start(_ => OkResponse);
        using var release = new ManualResetEventSlim();
        var lease = new Lease();
        using var transport = CreateTransport(server.PipeName, options: new NamedPipeBrokerTransportOptions
        {
            ServerAuthenticatorTimeout = TimeSpan.FromMilliseconds(50),
            ServerAuthenticator = (_, _) =>
            {
                release.Wait(TimeSpan.FromSeconds(10));
                return ValueTask.FromResult<IDisposable?>(lease);
            },
        });

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => transport.Send(HealthRequest));

        Assert.Equal(BrokerClientErrorKind.Timeout, ex.Kind);
        release.Set();
        await lease.WaitDisposed();
        Assert.Equal([""], await server.Completed);
    }

    [Fact]
    public async Task Server_authenticator_own_cancellation_is_a_rejection()
    {
        await using var server = TestPipeServer.Start(_ => OkResponse);
        using var transport = CreateTransport(server.PipeName, options: new NamedPipeBrokerTransportOptions
        {
            ServerAuthenticator = (_, _) => ValueTask.FromCanceled<IDisposable?>(new CancellationToken(canceled: true)),
        });

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => transport.Send(HealthRequest));

        Assert.Equal(BrokerClientErrorKind.ServerVerificationFailed, ex.Kind);
        Assert.Equal([""], await server.Completed);
    }

    [Fact]
    public async Task Connect_timeout_is_configurable()
    {
        using var transport = CreateTransport(
            "now-test-missing-" + Guid.NewGuid().ToString("N")[..12],
            options: new NamedPipeBrokerTransportOptions { ConnectTimeout = TimeSpan.FromMilliseconds(100) });

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => transport.Send(HealthRequest));

        Assert.Equal(BrokerClientErrorKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task Response_timeout_is_configurable()
    {
        await using var server = TestPipeServer.Start(_ => null);
        using var transport = CreateTransport(
            server.PipeName,
            options: new NamedPipeBrokerTransportOptions { ResponseTimeout = TimeSpan.FromMilliseconds(100) });

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => transport.Send(HealthRequest));

        Assert.Equal(BrokerClientErrorKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task Response_body_budget_is_configurable()
    {
        await using var server = TestPipeServer.Start(_ => "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\n123");
        using var transport = CreateTransport(server.PipeName, options: new NamedPipeBrokerTransportOptions { MaxResponseBodyBytes = 2 });

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => transport.Send(HealthRequest));

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, ex.Kind);
        Assert.Contains("2-byte response size limit", ex.Message);
    }

    [Fact]
    public async Task Broker_client_uses_default_transport_options()
    {
        await using var server = TestPipeServer.Start(_ =>
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\n{}");
        using var client = new BrokerClient(new BrokerClientOptions
        {
            RequestedElevation = Elevation.Standard,
            PipeName = server.PipeName,
            ClientExecutablePath = "C:\\Tools\\client.exe",
            NamedPipeTransport = new NamedPipeBrokerTransportOptions { VerifyServer = false },
        });

        Assert.True(await client.IsAvailable());
    }

    [Fact]
    public async Task Broker_client_default_transport_verifies_the_server()
    {
        await using var server = TestPipeServer.Start(_ => OkResponse);
        var traces = new List<string>();
        using var client = new BrokerClient(new BrokerClientOptions
        {
            RequestedElevation = Elevation.Standard,
            PipeName = server.PipeName,
            ClientExecutablePath = "C:\\Tools\\client.exe",
        })
        {
            Trace = traces.Add,
        };

        Assert.False(await client.IsAvailable());
        Assert.Contains(traces, trace => trace.Contains("could not be verified", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pipe_is_opened_without_generic_write_access()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var server = TestPipeServer.Start(_ => OkResponse, security: ClientAccessOnlySecurity());

        using (var legacyClient = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut))
        {
            Assert.Throws<UnauthorizedAccessException>(() => legacyClient.Connect(1000));
        }

        using var transport = CreateTransport(server.PipeName);
        var response = await transport.Send(HealthRequest);

        Assert.Equal(200, response.StatusCode);
    }

    [Theory]
    [InlineData(TokenImpersonationLevel.Identification)]
    [InlineData(TokenImpersonationLevel.Impersonation)]
    public async Task Pipe_grants_the_configured_impersonation_level(TokenImpersonationLevel level)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Load the identity types before impersonating: an identification-level token cannot open assembly files.
        _ = WindowsIdentity.GetCurrent().ImpersonationLevel;

        TokenImpersonationLevel? observed = null;
        await using var server = TestPipeServer.Start(
            _ => OkResponse,
            onConnected: pipe => pipe.RunAsClient(() => observed = OperatingSystem.IsWindows() ? GetThreadImpersonationLevel() : null));
        using var transport = CreateTransport(server.PipeName, options: new NamedPipeBrokerTransportOptions { ImpersonationLevel = level });

        await transport.Send(HealthRequest);
        await server.Completed;

        Assert.Equal(level, observed);
    }

    private static NamedPipeBrokerTransport CreateTransport(
        string pipeName,
        List<TimeSpan>? delays = null,
        NamedPipeBrokerTransportOptions? options = null)
    {
        options = (options ?? new NamedPipeBrokerTransportOptions()) with { PipeName = pipeName, VerifyServer = false };
        return new NamedPipeBrokerTransport(options, (delay, _) =>
        {
            delays?.Add(delay);
            return Task.CompletedTask;
        });
    }

    [SupportedOSPlatform("windows")]
    private static TokenImpersonationLevel? GetThreadImpersonationLevel() =>
        WindowsIdentity.GetCurrent(ifImpersonating: true)?.ImpersonationLevel;

    [SupportedOSPlatform("windows")]
    private static PipeSecurity ClientAccessOnlySecurity()
    {
        // Mirrors the broker pipe DACL: read, write data and write attributes, without the right to create instances.
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!,
            PipeAccessRights.Read | PipeAccessRights.Synchronize | PipeAccessRights.WriteData | PipeAccessRights.WriteAttributes,
            AccessControlType.Allow));
        return security;
    }

    private sealed class Lease : IDisposable
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed => _disposed.Task.IsCompleted;

        public Task WaitDisposed() => _disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Dispose() => _disposed.TrySetResult();
    }

    /// <summary>Minimal broker stand-in: serves scripted responses to a fixed number of connections.</summary>
    private sealed class TestPipeServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private int _receivedBytes;

        private TestPipeServer(string pipeName)
        {
            PipeName = pipeName;
        }

        public string PipeName { get; }

        public int ReceivedBytes => Volatile.Read(ref _receivedBytes);

        /// <summary>Requests received by each connection, in order; empty when the client sent nothing.</summary>
        public Task<List<string>> Completed { get; private set; } = null!;

        public static TestPipeServer Start(
            Func<int, string?> respond,
            int connections = 1,
            PipeSecurity? security = null,
            Action<NamedPipeServerStream>? onConnected = null)
        {
            var server = new TestPipeServer("now-test-" + Guid.NewGuid().ToString("N")[..12]);
            var first = server.CreateInstance(security);
            server.Completed = Task.Run(() => server.Serve(first, respond, connections, security, onConnected));
            return server;
        }

        private NamedPipeServerStream CreateInstance(PipeSecurity? security)
        {
            if (security is not null && OperatingSystem.IsWindows())
            {
                return NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0,
                    0,
                    security);
            }

            return new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        private async Task<List<string>> Serve(
            NamedPipeServerStream first,
            Func<int, string?> respond,
            int connections,
            PipeSecurity? security,
            Action<NamedPipeServerStream>? onConnected)
        {
            var requests = new List<string>();
            var next = first;
            for (var index = 0; index < connections; index++)
            {
                using var current = next;
                try
                {
                    await current.WaitForConnectionAsync(_cts.Token);
                }
                catch (IOException)
                {
                    // The client connected and already closed the pipe without sending anything.
                    next = index + 1 < connections ? CreateInstance(security) : null!;
                    requests.Add("");
                    continue;
                }

                next = index + 1 < connections ? CreateInstance(security) : null!;

                var request = await ReadRequest(current);
                if (request.Length > 0)
                {
                    onConnected?.Invoke(current);
                }

                requests.Add(request);

                var response = request.Length > 0 ? respond(index) : null;
                if (response is null)
                {
                    if (request.Length > 0)
                    {
                        await Task.Delay(Timeout.Infinite, _cts.Token).ContinueWith(_ => { }, TaskScheduler.Default);
                    }

                    continue;
                }

                await current.WriteAsync(Encoding.UTF8.GetBytes(response), _cts.Token);
                await current.FlushAsync(_cts.Token);
            }

            return requests;
        }

        private async Task<string> ReadRequest(Stream stream)
        {
            var received = new MemoryStream();
            var buffer = new byte[4096];
            int? expected = null;
            while (expected is null || received.Length < expected)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer, _cts.Token);
                }
                catch (IOException)
                {
                    read = 0;
                }

                if (read == 0)
                {
                    break;
                }

                received.Write(buffer, 0, read);
                Interlocked.Add(ref _receivedBytes, read);

                var text = Encoding.UTF8.GetString(received.ToArray());
                var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (expected is null && headerEnd >= 0)
                {
                    var lengthLine = text[..headerEnd].Split("\r\n").Single(line => line.StartsWith("Content-Length:", StringComparison.Ordinal));
                    expected = headerEnd + 4 + int.Parse(lengthLine["Content-Length:".Length..].Trim());
                }
            }

            return Encoding.UTF8.GetString(received.ToArray());
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try
            {
                await Completed.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }
}