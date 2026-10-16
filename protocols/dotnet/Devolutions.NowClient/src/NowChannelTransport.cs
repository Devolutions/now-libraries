using Devolutions.NowClient.Execution;
using Devolutions.NowProto;

namespace Devolutions.NowClient
{
    /// <summary>
    /// Transport wrapper with NOW-proto message
    /// serialization/deserialization capabilities.
    /// Includes defragmentation logic to handle partial messages and multiple in-frame messages.
    /// </summary>
    internal class NowChannelTransport(
        INowTransport transport,
        DedicatedExecution? execution = null,
        CancellationToken lifetimeToken = default) : IDisposable
    {
        private readonly NowMessageBuffer _messageBuffer = new();

        public async Task WriteMessage(INowSerialize message)
        {
            lifetimeToken.ThrowIfCancellationRequested();
            var cursor = new NowWriteCursor(_writeBuffer);
            message.Serialize(cursor);

            var bytesFilled = checked((int)cursor.BytesFilled);
            await ProtocolTasks.WaitAsync(transport.Write(_writeBuffer[0..bytesFilled]), execution, lifetimeToken);
        }

        public async Task<T> ReadMessage<T>(CancellationToken cancellationToken = default) where T : INowDeserialize<T>
        {
            var message = await ReadMessageAny(cancellationToken);
            return message.Deserialize<T>();
        }

        public async Task<NowMessage.NowMessageView> ReadMessageAny(CancellationToken cancellationToken = default)
        {
            using var linked = cancellationToken.CanBeCanceled && lifetimeToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken)
                : null;
            var readToken = linked?.Token ?? (cancellationToken.CanBeCanceled ? cancellationToken : lifetimeToken);
            // Keep reading from transport until we have at least one complete message.
            while (!_messageBuffer.HasCompleteMessage)
            {
                if (execution != null)
                    await Task.Yield();
                readToken.ThrowIfCancellationRequested();
                var frame = await ProtocolTasks.WaitAsync(transport.Read(readToken), execution, readToken);
                if (frame.Length == 0)
                {
                    throw new EndOfStreamException("NOW transport closed before a complete message was received.");
                }
                _messageBuffer.AddData(frame);
            }

            return _messageBuffer.GetNextMessage()!.Value;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                transport.Dispose();
            }
        }

        private const int DefaultBufferSize = 1024 * 64; // 64KB Buffer
        private readonly byte[] _writeBuffer = new byte[DefaultBufferSize];
        private int _disposed;
    }
}