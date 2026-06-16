namespace Devolutions.NowClient;

using System.IO.Pipes;

/// <summary>
/// Pipe transport implementation for server-side named pipe.
/// </summary>
internal class NowRdmHostPipeTransport : INowTransport
{
    private readonly NamedPipeServerStream _pipe;
    private readonly byte[] _buffer = new byte[64 * 1024];

    public NowRdmHostPipeTransport(NamedPipeServerStream pipe)
    {
        _pipe = pipe;
        _pipe.ReadMode = PipeTransmissionMode.Byte;
    }

    public async Task Write(byte[] data)
    {
        await _pipe.WriteAsync(data);
        await _pipe.FlushAsync();
    }

    public async Task<byte[]> Read(CancellationToken cancellationToken)
    {
        var bytesRead = await _pipe.ReadAsync(_buffer, cancellationToken);

        if (bytesRead == 0)
        {
            throw new EndOfStreamException("Pipe closed by client");
        }

        return _buffer[..bytesRead];
    }

    public void Dispose()
    {
        _pipe.Dispose();
    }
}