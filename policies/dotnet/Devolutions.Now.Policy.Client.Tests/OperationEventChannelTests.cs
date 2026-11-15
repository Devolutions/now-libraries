using System.IO.Pipes;

using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

/// <summary>
/// Tests for <see cref="BrokerClient.OpenEventChannel"/> and
/// <see cref="OperationEventChannel"/> using a real local named pipe carrying the shared
/// binary frame fixture.
/// </summary>
public class OperationEventChannelTests
{
    private static string FramesFixturePath =>
        Path.Combine(TestData.SamplesDir, "frames", "event-channel.frames.bin");

    [Fact]
    public async Task OpenEventChannel_reads_events_pushed_over_local_pipe()
    {
        var pipeName = RandomPipeName();
        using var server = ServeFixtureBytes(pipeName, File.ReadAllBytes(FramesFixturePath));
        using var client = CreateClient();

        await using var channel = await client.OpenEventChannel(CreateExecutionResponse(pipeName));

        var frames = new List<EventFrame>();
        await foreach (var frame in channel.ReadEvents())
        {
            frames.Add(frame);
        }

        // ReadEvents skips the Unknown frame in the fixture and completes after Finish.
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
            f => Assert.IsType<EventFrame.Finish>(f));
    }

    [Fact]
    public async Task ReadFrame_returns_unknown_frames_and_null_at_end_of_stream()
    {
        var pipeName = RandomPipeName();
        using var server = ServeFixtureBytes(pipeName, File.ReadAllBytes(FramesFixturePath));
        using var client = CreateClient();

        await using var channel = await client.OpenEventChannel(CreateExecutionResponse(pipeName));

        var frames = new List<EventFrame>();
        while (await channel.ReadFrame() is { } frame)
        {
            frames.Add(frame);
        }

        Assert.Equal(8, frames.Count);
        Assert.Equal(0x7fff, Assert.IsType<EventFrame.Unknown>(frames[6]).Kind);
        Assert.IsType<EventFrame.Finish>(frames[^1]);
    }

    [Fact]
    public async Task OpenEventChannel_rejects_response_without_event_channel()
    {
        using var client = CreateClient();
        var response = CreateExecutionResponse(pipeName: null);

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.OpenEventChannel(response));
        Assert.Equal(BrokerClientErrorKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public async Task OpenEventChannel_rejects_empty_channel_path()
    {
        using var client = CreateClient();
        var response = CreateExecutionResponse(pipeName: "");

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.OpenEventChannel(response));
        Assert.Equal(BrokerClientErrorKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public async Task ReadFrame_returns_null_when_server_closes_pipe_at_frame_boundary()
    {
        var pipeName = RandomPipeName();
        // Only Hello and one stdout frame; the server closes the pipe without sending Finish.
        var bytes = new EventFrame.Hello(1, 0).Encode()
            .Concat(new EventFrame.Stdout("partial run\n").Encode())
            .ToArray();
        using var server = ServeFixtureBytes(pipeName, bytes);
        using var client = CreateClient();

        await using var channel = await client.OpenEventChannel(CreateExecutionResponse(pipeName));

        Assert.IsType<EventFrame.Hello>(await channel.ReadFrame());
        Assert.IsType<EventFrame.Stdout>(await channel.ReadFrame());
        Assert.Null(await channel.ReadFrame());
    }

    [Fact]
    public async Task ReadEvents_completes_without_finish_when_server_closes_pipe()
    {
        var pipeName = RandomPipeName();
        var bytes = new EventFrame.Hello(1, 0).Encode()
            .Concat(new EventFrame.StatusUpdated().Encode())
            .ToArray();
        using var server = ServeFixtureBytes(pipeName, bytes);
        using var client = CreateClient();

        await using var channel = await client.OpenEventChannel(CreateExecutionResponse(pipeName));

        var frames = new List<EventFrame>();
        await foreach (var frame in channel.ReadEvents())
        {
            frames.Add(frame);
        }

        Assert.Equal(2, frames.Count);
        Assert.DoesNotContain(frames, f => f is EventFrame.Finish);
    }

    [Fact]
    public async Task ReadFrame_throws_when_server_closes_pipe_mid_frame()
    {
        var pipeName = RandomPipeName();
        // A complete Hello followed by a truncated stdout frame: the header announces
        // an 11-byte body but the server closes the pipe after 4 body bytes.
        var truncated = new EventFrame.Stdout("interrupted").Encode()[..10];
        var bytes = new EventFrame.Hello(1, 0).Encode().Concat(truncated).ToArray();
        using var server = ServeFixtureBytes(pipeName, bytes);
        using var client = CreateClient();

        await using var channel = await client.OpenEventChannel(CreateExecutionResponse(pipeName));

        Assert.IsType<EventFrame.Hello>(await channel.ReadFrame());
        await Assert.ThrowsAsync<EventFrameException>(() => channel.ReadFrame());
    }

    [Fact]
    public async Task ReadFrame_rejects_stream_that_does_not_start_with_hello()
    {
        var pipeName = RandomPipeName();
        var bytes = new EventFrame.Stdout("data before hello\n").Encode();
        using var server = ServeFixtureBytes(pipeName, bytes);
        using var client = CreateClient();

        await using var channel = await client.OpenEventChannel(CreateExecutionResponse(pipeName));

        var ex = await Assert.ThrowsAsync<EventFrameException>(() => channel.ReadFrame());
        Assert.Contains("Hello", ex.Message);
    }

    [Fact]
    public async Task ReadFrame_rejects_unsupported_protocol_major_version()
    {
        var pipeName = RandomPipeName();
        var bytes = new EventFrame.Hello((ushort)(EventChannelProtocol.VersionMajor + 1), 0).Encode();
        using var server = ServeFixtureBytes(pipeName, bytes);
        using var client = CreateClient();

        await using var channel = await client.OpenEventChannel(CreateExecutionResponse(pipeName));

        var ex = await Assert.ThrowsAsync<EventFrameException>(() => channel.ReadFrame());
        Assert.Contains("major version", ex.Message);
    }

    // oracle: contract Devolutions/devolutions-gateway@e0e75f634940:crates/agent-channel-proto/CONTRACT.package-management.md §operations
    [Fact]
    public async Task OpenGrantEventChannel_does_not_claim_no_work_started_for_response_from_another_client()
    {
        using var submittingClient = BrokerClientTests.CreateGrantClient(new FakeAuthenticatedTransport());
        var response = await submittingClient.ExecuteGrant(BrokerClientTests.GrantRequest(), "grant-1");
        using var client = BrokerClientTests.CreateGrantClient(new FakeAuthenticatedTransport());

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.OpenGrantEventChannel(response));

        Assert.Equal(BrokerClientErrorKind.InvalidRequest, ex.Kind);
        Assert.False(ex.NoWorkStarted);
    }

    [Fact]
    public async Task OpenGrantEventChannel_reports_unavailable_output_capture_without_event_channel()
    {
        using var client = BrokerClientTests.CreateGrantClient(new FakeAuthenticatedTransport());
        var response = await client.ExecuteGrant(BrokerClientTests.GrantRequest(), "grant-1");

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.OpenGrantEventChannel(response));

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, ex.Kind);
        Assert.Contains("output capture is unavailable", ex.Message);
    }

    [Theory]
    [InlineData(@"\\remote\pipe\nowec-x")]
    [InlineData(@"\\.\pipe\nested\nowec-x")]
    [InlineData("nested/nowec-x")]
    public async Task OpenGrantEventChannel_rejects_non_local_pipe_paths(string path)
    {
        using var client = BrokerClientTests.CreateGrantClient(new FakeAuthenticatedTransport(GrantResponse(path)));
        var response = await client.ExecuteGrant(BrokerClientTests.GrantRequest(), "grant-1");

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.OpenGrantEventChannel(response));

        Assert.Equal(BrokerClientErrorKind.InvalidResponse, ex.Kind);
    }

    [WindowsFact]
    public async Task OpenGrantEventChannel_rejects_event_pipe_served_by_another_process_before_reading()
    {
        var pipeName = RandomPipeName();
        using var server = ServeFixtureBytes(pipeName, GrantFrames());
        var authenticator = new RecordingAuthenticator();
        var accepted = new BrokerPipeServerIdentity(Environment.ProcessId + 1, 0);
        using var client = BrokerClientTests.CreateGrantClient(new FakeAuthenticatedTransport(GrantResponse(pipeName), accepted));
        var response = await client.ExecuteGrant(BrokerClientTests.GrantRequest(), "grant-1");

        var ex = await Assert.ThrowsAsync<BrokerClientException>(() => client.OpenGrantEventChannel(response));

        Assert.Equal(BrokerClientErrorKind.ServerAuthenticationFailed, ex.Kind);
        Assert.Null(authenticator.Seen);
    }

    [WindowsFact]
    public async Task OpenGrantEventChannel_reads_complete_capture_from_the_accepting_server()
    {
        var pipeName = RandomPipeName();
        using var server = ServeFixtureBytes(pipeName, GrantFrames());
        var accepted = new BrokerPipeServerIdentity(Environment.ProcessId, System.Diagnostics.Process.GetCurrentProcess().SessionId);
        using var client = BrokerClientTests.CreateGrantClient(new FakeAuthenticatedTransport(GrantResponse(pipeName), accepted));
        var response = await client.ExecuteGrant(BrokerClientTests.GrantRequest(), "grant-1");

        await using var channel = await client.OpenGrantEventChannel(response);
        await foreach (var _ in channel.ReadEvents())
        {
        }

        Assert.True(channel.IsCaptureComplete);
    }

    [Fact]
    public async Task IsCaptureComplete_is_true_only_after_hello_and_finish()
    {
        await using var channel = GrantChannel(new EventFrame.Hello(1, 0), new EventFrame.Stdout("out\n"));
        await Drain(channel, expectEof: false);
        Assert.False(channel.IsCaptureComplete);

        await using var finished = GrantChannel(new EventFrame.Hello(1, 0), new EventFrame.Stdout("out\n"), new EventFrame.Finish());
        await Drain(finished);
        Assert.True(finished.IsCaptureComplete);
    }

    [Theory]
    [InlineData("stdout")]
    [InlineData("stderr")]
    public async Task IsCaptureComplete_is_false_after_overflow(string stream)
    {
        EventFrame overflow = stream == "stdout" ? new EventFrame.StdoutOverflow(10) : new EventFrame.StderrOverflow(10);
        await using var channel = GrantChannel(new EventFrame.Hello(1, 0), overflow, new EventFrame.Finish());

        await Drain(channel);

        Assert.False(channel.IsCaptureComplete);
    }

    [Fact]
    public async Task Grant_channel_throws_on_eof_before_finish_but_legacy_channel_returns_null()
    {
        var bytes = new EventFrame.Hello(1, 0).Encode();

        await using var grant = new OperationEventChannel(new MemoryStream(bytes), completeCapture: true);
        Assert.IsType<EventFrame.Hello>(await grant.ReadFrame());
        var ex = await Assert.ThrowsAsync<EventFrameException>(() => grant.ReadFrame());
        Assert.Contains("incomplete", ex.Message);
        Assert.False(grant.IsCaptureComplete);

        await using var legacy = new OperationEventChannel(new MemoryStream(bytes));
        Assert.IsType<EventFrame.Hello>(await legacy.ReadFrame());
        Assert.Null(await legacy.ReadFrame());
    }

    [Fact]
    public async Task IsCaptureComplete_is_false_after_malformed_frame()
    {
        var bytes = new EventFrame.Hello(1, 0).Encode().AsEnumerable().Concat(new EventFrame.Finish().Encode()).Concat(new byte[] { 0xff }).ToArray();
        await using var channel = new OperationEventChannel(new MemoryStream(bytes), completeCapture: true);

        await Assert.ThrowsAsync<EventFrameException>(async () =>
        {
            while (await channel.ReadFrame() is not null)
            {
            }
        });

        Assert.False(channel.IsCaptureComplete);
    }

    // oracle: spec policies/docs/event-channel-protocol.md §NOW_BROKER_FINISH (0x0002)
    [Fact]
    public async Task Grant_channel_rejects_output_buffered_after_finish()
    {
        await using var channel = GrantChannel(
            new EventFrame.Hello(1, 0), new EventFrame.Finish(), new EventFrame.Stdout("late"));

        await Assert.ThrowsAsync<EventFrameException>(async () =>
        {
            await foreach (var _ in channel.ReadEvents())
            {
            }
        });
        Assert.False(channel.IsCaptureComplete);
    }

    // oracle: spec policies/docs/event-channel-protocol.md §NOW_BROKER_HELLO (0x0000)
    [Fact]
    public async Task Grant_channel_rejects_a_second_hello()
    {
        await using var channel = GrantChannel(new EventFrame.Hello(1, 0), new EventFrame.Hello(1, 0), new EventFrame.Finish());

        Assert.IsType<EventFrame.Hello>(await channel.ReadFrame());
        await Assert.ThrowsAsync<EventFrameException>(() => channel.ReadFrame());
        Assert.False(channel.IsCaptureComplete);
    }

    // oracle: spec policies/docs/event-channel-protocol.md §NOW_BROKER_FINISH (0x0002)
    [Fact]
    public async Task Grant_channel_rejects_output_read_after_finish()
    {
        var bytes = new EventFrame.Hello(1, 0).Encode()
            .Concat(new EventFrame.Finish().Encode())
            .Concat(new EventFrame.Stdout("late").Encode())
            .ToArray();
        await using var channel = new OperationEventChannel(new OneByteReadStream(bytes), completeCapture: true);

        Assert.IsType<EventFrame.Hello>(await channel.ReadFrame());
        Assert.IsType<EventFrame.Finish>(await channel.ReadFrame());
        await Assert.ThrowsAsync<EventFrameException>(() => channel.ReadFrame());
        Assert.False(channel.IsCaptureComplete);
    }

    // oracle: reference Devolutions/now-libraries@27729ec917c4:policies/dotnet/Devolutions.Now.Policy.Client/OperationEventChannel.cs
    [Fact]
    public async Task Legacy_channel_preserves_frames_after_finish()
    {
        var bytes = new EventFrame.Hello(1, 0).Encode()
            .Concat(new EventFrame.Finish().Encode())
            .Concat(new EventFrame.Stdout("late").Encode())
            .ToArray();
        await using var channel = new OperationEventChannel(new MemoryStream(bytes));

        Assert.IsType<EventFrame.Hello>(await channel.ReadFrame());
        Assert.IsType<EventFrame.Finish>(await channel.ReadFrame());
        Assert.IsType<EventFrame.Stdout>(await channel.ReadFrame());
    }

    // oracle: contract Devolutions/devolutions-gateway@e0e75f634940:crates/agent-channel-proto/CONTRACT.package-management.md §operations
    [Fact]
    public async Task Grant_channel_marks_capture_incomplete_after_read_failure()
    {
        var bytes = new EventFrame.Hello(1, 0).Encode().Concat(new EventFrame.Finish().Encode()).ToArray();
        await using var channel = new OperationEventChannel(new FailAtEndStream(bytes, new IOException("read failed")), completeCapture: true);

        Assert.IsType<EventFrame.Hello>(await channel.ReadFrame());
        Assert.IsType<EventFrame.Finish>(await channel.ReadFrame());
        await Assert.ThrowsAsync<IOException>(() => channel.ReadFrame());
        Assert.False(channel.IsCaptureComplete);
    }

    // oracle: contract Devolutions/devolutions-gateway@e0e75f634940:crates/agent-channel-proto/CONTRACT.package-management.md §operations
    [Fact]
    public async Task Grant_channel_marks_capture_incomplete_after_read_cancellation()
    {
        var bytes = new EventFrame.Hello(1, 0).Encode().Concat(new EventFrame.Finish().Encode()).ToArray();
        await using var channel = new OperationEventChannel(new FailAtEndStream(bytes, new OperationCanceledException()), completeCapture: true);

        Assert.IsType<EventFrame.Hello>(await channel.ReadFrame());
        Assert.IsType<EventFrame.Finish>(await channel.ReadFrame());
        await Assert.ThrowsAsync<OperationCanceledException>(() => channel.ReadFrame());
        Assert.False(channel.IsCaptureComplete);
    }

    private sealed class OneByteReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }

    private sealed class FailAtEndStream(byte[] bytes, Exception exception) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Position == Length
                ? ValueTask.FromException<int>(exception)
                : base.ReadAsync(buffer, cancellationToken);
    }

    private static byte[] GrantFrames() =>
        new EventFrame.Hello(1, 0).Encode().AsEnumerable().Concat(new EventFrame.Finish().Encode()).ToArray();

    private static BrokerTransportResponse GrantResponse(string pipeName) =>
        new() { StatusCode = 200, Body = BrokerClientTests.GrantResponseJson(pipeName) };

    private static OperationEventChannel GrantChannel(params EventFrame[] frames) =>
        new(new MemoryStream(frames.SelectMany(frame => frame.Encode()).ToArray()), completeCapture: true);

    private static async Task Drain(OperationEventChannel channel, bool expectEof = true)
    {
        while (true)
        {
            EventFrame? frame;
            try
            {
                frame = await channel.ReadFrame();
            }
            catch (EventFrameException) when (!expectEof)
            {
                return;
            }

            if (frame is null or EventFrame.Finish)
            {
                return;
            }
        }
    }
    // Keep the name short: on macOS named pipes map to unix domain socket paths
    // (temp dir + "CoreFxPipe_" prefix) limited to 104 characters.
    private static string RandomPipeName() =>
        $"nowec-{Guid.NewGuid():N}"[..22];

    private static ExecutionResponse CreateExecutionResponse(string? pipeName) => new()
    {
        Operation = new OperationSubmission
        {
            OperationId = "op-test-000001",
            EventChannel = pipeName is null
                ? null
                : new EventChannel { Kind = EventChannelKind.LocalPipe, Path = pipeName },
        },
    };

    private static BrokerClient CreateClient() => new(new BrokerClientOptions
    {
        EffectiveUser = "DEVOLUTIONS\\bob",
        RequestedElevation = Elevation.Standard,
        ClientExecutablePath = "C:\\Tools\\client.exe",
        ClientVersion = "9.8.7",
    });

    /// <summary>
    /// Start a one-shot pipe server that writes <paramref name="bytes"/> to the first
    /// connected client and then closes the pipe.
    /// </summary>
    private static IDisposable ServeFixtureBytes(string pipeName, byte[] bytes)
    {
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.Out,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        _ = Task.Run(async () =>
        {
            await using (server)
            {
                await server.WaitForConnectionAsync();
                await server.WriteAsync(bytes);
                await server.FlushAsync();
            }
        });

        return server;
    }
}