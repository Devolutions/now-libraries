using Devolutions.NowProto;

namespace Devolutions.NowClient
{
    /// <summary>
    /// A buffer that accumulates incoming bytes from INowTransport and reconstructs complete NowMessages.
    /// Handles fragmentation scenarios including incomplete messages, multiple messages per frame,
    /// and mixed complete/partial messages.
    /// Uses a simple linear buffer with compaction.
    /// </summary>
    internal class NowMessageBuffer
    {
        private const int DefaultBufferSize = 64 * 1024; // 64KB initial buffer.
        private const int MaxBufferSize = 1024 * 1024; // 1MB max buffer size.

        private byte[] _buffer;
        private int _dataLength; // Amount of valid data in buffer.
        private readonly Queue<NowMessage.NowMessageView> _completeMessages = new();

        public NowMessageBuffer(int initialBufferSize = DefaultBufferSize)
        {
            _buffer = new byte[initialBufferSize];
            _dataLength = 0;
        }

        /// <summary>
        /// Adds incoming data from transport and processes it to extract complete messages.
        /// </summary>
        /// <param name="data">Raw bytes received from INowTransport</param>
        public void AddData(byte[] data)
        {
            if (data.Length == 0)
                return;

            // Ensure we have enough space for the new data.
            EnsureCapacity(data.Length);

            // Append new data to the end of existing data.
            Array.Copy(data, 0, _buffer, _dataLength, data.Length);
            _dataLength += data.Length;

            // Process all complete messages.
            ProcessBuffer();
        }

        /// <summary>
        /// Ensures the buffer has enough space for additional data.
        /// Expands the buffer if necessary.
        /// </summary>
        private void EnsureCapacity(int additionalLength)
        {
            var requiredCapacity = _dataLength + additionalLength;
            if (requiredCapacity <= _buffer.Length)
                return;

            var newSize = Math.Max(_buffer.Length * 2, requiredCapacity);
            newSize = Math.Min(newSize, MaxBufferSize);

            if (newSize < requiredCapacity)
                throw new InvalidOperationException($"Message buffer size limit exceeded. Required: {requiredCapacity}, Max: {MaxBufferSize}");

            var newBuffer = new byte[newSize];
            if (_dataLength > 0)
            {
                Array.Copy(_buffer, 0, newBuffer, 0, _dataLength);
            }
            _buffer = newBuffer;
        }

        /// <summary>
        /// Processes the internal buffer to extract complete messages.
        /// Removes processed messages and compacts remaining data to buffer start.
        /// </summary>
        private void ProcessBuffer()
        {
            int processedBytes = 0;

            while (true)
            {
                var remainingData = _dataLength - processedBytes;

                var bufferSegment = new ArraySegment<byte>(_buffer, processedBytes, remainingData);
                if (!NowMessage.IsInputHasEnoughBytes(bufferSegment))
                    break;

                // Extract the complete message.
                var cursor = new NowReadCursor(bufferSegment);
                var messageView = NowMessage.Read(cursor);

                // Add complete message to queue.
                _completeMessages.Enqueue(messageView);

                // Move to next message.
                processedBytes += (int)messageView.FrameSize;
            }

            if (processedBytes == 0)
            {
                return;
            }

            var remainingBytes = _dataLength - processedBytes;

            if (remainingBytes == 0)
            {
                // All data processed, reset buffer.
                _dataLength = 0;
                return;
            }

            // Compact buffer - move any remaining incomplete data to the beginning
            // of the buffer if buffer still exceeds DefaultBufferSize.
            if (remainingBytes > DefaultBufferSize)
            {
                Array.Copy(_buffer, processedBytes, _buffer, 0, remainingBytes);
                _dataLength = remainingBytes;
                return;
            }
            // Shrink buffer when its size drops below DefaultBufferSize.
            var newBuffer = new byte[DefaultBufferSize];
            Array.Copy(_buffer, processedBytes, newBuffer, 0, remainingBytes);
            _buffer = newBuffer;
            _dataLength = remainingBytes;
        }

        /// <summary>
        /// Checks if there are any complete messages ready for consumption.
        /// </summary>
        public bool HasCompleteMessage => _completeMessages.Count > 0;

        /// <summary>
        /// Gets the next complete message if available.
        /// </summary>
        /// <returns>Complete message or null if none available</returns>
        public NowMessage.NowMessageView? GetNextMessage()
        {
            return HasCompleteMessage ? _completeMessages.Dequeue() : null;
        }
    }
}