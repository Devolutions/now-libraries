using Devolutions.NowProto.Exceptions;

namespace Devolutions.NowProto
{
    /// <summary>
    /// Provides utilities for reading data from a buffer.
    /// </summary>
    public class NowReadCursor(ArraySegment<byte> buffer)
    {
        public ReadOnlySpan<byte> Remaining()
        {
            return _buffer;
        }

        public void EnsureEnoughBytes(uint length)
        {
            if ((uint)_buffer.Count < length)
            {
                throw new NowDecodeException(NowDecodeException.ErrorKind.NotEnoughBytes);
            }
        }

        public byte ReadByte()
        {
            var data = ReadBytes(1);
            return data[0];
        }

        public ushort ReadUInt16Le()
        {
            var data = ReadBytes(2);

            if (BitConverter.IsLittleEndian) return BitConverter.ToUInt16(data);

            Span<byte> reversed = [data[1], data[0]];
            return BitConverter.ToUInt16(reversed);

        }

        public uint ReadUInt32Le()
        {
            var data = ReadBytes(4);

            if (BitConverter.IsLittleEndian) return BitConverter.ToUInt32(data);
            Span<byte> reversed = [data[3], data[2], data[1], data[0]];
            return BitConverter.ToUInt32(reversed);

        }

        public ulong ReadUInt64Le()
        {
            var data = ReadBytes(8);

            if (BitConverter.IsLittleEndian) return BitConverter.ToUInt64(data);
            Span<byte> reversed = [data[7], data[6], data[5], data[4], data[3], data[2], data[1], data[0]];
            return BitConverter.ToUInt64(reversed);
        }

        public void Advance(uint count)
        {
            var remaining = _buffer.Slice((int)count, _buffer.Count - (int)count);
            _buffer = remaining;
        }

        public bool IsEmpty()
        {
            return _buffer.Count == 0;
        }

        public ArraySegment<byte> ReadBytes(uint count)
        {
            EnsureEnoughBytes(count);

            var splitPosition = (int)count;

            var data = _buffer[..splitPosition];
            var remaining = _buffer.Slice(splitPosition, _buffer.Count - splitPosition);
            _buffer = remaining;

            return data;
        }

        private ArraySegment<byte> _buffer = buffer;
    }
}