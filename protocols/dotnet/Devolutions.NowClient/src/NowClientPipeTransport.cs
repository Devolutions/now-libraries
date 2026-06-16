namespace Devolutions.NowClient;

using System.IO.Pipes;

/// <summary>
/// Implements NowProto packets transport over named pipes.
/// </summary>
public class NowClientPipeTransport : INowTransport
{
    private NowClientPipeTransport(NamedPipeClientStream pipe)
    {
        this._pipe = pipe;
    }

    public static async Task<NowClientPipeTransport> Connect(string pipeName, TimeSpan? timeout = null)
    {
        if (pipeName.Length == 0)
        {
            throw new ArgumentException("Pipe name cannot be empty");
        }

        var pipeServer = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );

        if (timeout != null)
        {
            await pipeServer.ConnectAsync
            (
                timeout.Value,
                CancellationToken.None
            );
        }
        else
        {
            await pipeServer.ConnectAsync();
        }

        // Byte mode is default, but let's be explicit.
        pipeServer.ReadMode = PipeTransmissionMode.Byte;

        return new NowClientPipeTransport(pipeServer);
    }

    async Task INowTransport.Write(byte[] data)
    {
        await _pipe.WriteAsync(data);
        await _pipe.FlushAsync();
    }

    async Task<byte[]> INowTransport.Read(CancellationToken cancellationToken)
    {
        var bytesRead = await _pipe.ReadAsync(_buffer, cancellationToken);

        if (bytesRead == 0)
        {
            throw new EndOfStreamException("End of stream reached (DVC)");
        }

        return _buffer[..bytesRead];
    }

    public void Dispose()
    {
        _pipe.Dispose();
    }

    private readonly byte[] _buffer = new byte[64 * 1024];
    private readonly NamedPipeClientStream _pipe;
}