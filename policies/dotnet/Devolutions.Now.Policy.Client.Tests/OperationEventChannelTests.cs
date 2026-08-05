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