using System.Drawing;

using Devolutions.NowProto.Exceptions;
using Devolutions.NowProto.Types;

namespace Devolutions.NowProto.Messages
{
    /// <summary>
    /// The NOW_EXEC_DATA_MSG message is used to send input/output data as part of a remote execution.
    ///
    /// NOW-PROTO: NOW_EXEC_DATA_MSG
    /// </summary>
    public class NowMsgExecData : INowSerialize, INowDeserialize<NowMsgExecData>
    {
        // -- INowMessage --

        public static byte TypeMessageClass => NowMessage.ClassExec;
        public static byte TypeMessageKind => 0x05; // NOW-PROTO: NOW_EXEC_DATA_MSG_ID

        byte INowMessage.MessageClass => NowMessage.ClassExec;
        byte INowMessage.MessageKind => 0x05;

        // -- INowDeserialize --

        static NowMsgExecData INowDeserialize<NowMsgExecData>.Deserialize(ushort flags, NowReadCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            var msgFlags = (MsgFlags)flags;
            var sessionId = cursor.ReadUInt32Le();
            var data = cursor.ReadVarBuf();

            return new NowMsgExecData(sessionId, msgFlags, data);
        }

        // -- INowSerialize --

        ushort INowSerialize.Flags => (ushort)_flags;

        uint INowSerialize.BodySize => FixedPartSize + NowVarBuf.LengthOf(Data);

        void INowSerialize.SerializeBody(NowWriteCursor cursor)
        {
            cursor.EnsureEnoughBytes(FixedPartSize);
            cursor.WriteUint32Le(SessionId);
            cursor.WriteVarBuf(Data);
        }

        // -- impl --

        private const uint FixedPartSize = 4 /* u32 SessionId */;

        private NowMsgExecData(uint sessionId, MsgFlags flags, ArraySegment<byte> data)
        {
            SessionId = sessionId;
            _flags = flags;
            Data = data;
        }

        public NowMsgExecData(uint sessionId, StreamKind stream, bool last, ArraySegment<byte> data)
        {
            SessionId = sessionId;

            var flags = stream switch
            {
                StreamKind.Stdin => MsgFlags.Stdin,
                StreamKind.Stdout => MsgFlags.Stdout,
                StreamKind.Stderr => MsgFlags.Stderr,
                _ => throw new NowDecodeException(NowDecodeException.ErrorKind.InvalidDataStreamFlags)
            };

            if (last)
            {
                flags |= MsgFlags.Last;
            }

            _flags = flags;
            Data = data;
        }


        [Flags]
        private enum MsgFlags : ushort
        {
            None = 0x0000,
            /// <summary>
            /// This is the last data message, the command completed execution.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_DATA_LAST
            /// </summary>
            Last = 0x0001,
            /// <summary>
            /// The data is from the standard input.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_DATA_STDIN
            /// </summary>
            Stdin = 0x0002,
            /// <summary>
            /// The data is from the standard output.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_DATA_STDOUT
            /// </summary>
            Stdout = 0x0004,
            /// <summary>
            /// The data is from the standard error.
            ///
            /// NOW-PROTO: NOW_EXEC_FLAG_DATA_STDERR
            /// </summary>
            Stderr = 0x0008,
        }


        /// <summary>
        /// Data message stream kind.
        /// </summary>
        public enum StreamKind : ushort
        {
            Stdin = 0x0002, // Use save values as in MsgFlags
            Stdout = 0x0004,
            Stderr = 0x0008,
        }

        /// <summary>
        /// Standard io stream kind.
        ///
        /// PROTO: NOW_EXEC_FLAG_DATA_STDIN, NOW_EXEC_FLAG_DATA_STDOUT, NOW_EXEC_FLAG_DATA_STDERR
        /// </summary>
        public StreamKind Stream
        {
            get
            {
                StreamKind streamKind = (StreamKind)0;
                var streamFlagsCount = 0;

                var streamFlags = new[] { MsgFlags.Stdin, MsgFlags.Stdout, MsgFlags.Stderr };


                foreach (var kind in streamFlags)
                {
                    if (!_flags.HasFlag(kind)) continue;
                    streamKind = (StreamKind)kind;
                    ++streamFlagsCount;
                }

                return (streamFlagsCount == 1)
                    ? streamKind
                    : throw new NowDecodeException(NowDecodeException.ErrorKind.InvalidDataStreamFlags);
            }
        }

        /// <summary>
        /// This is the last data message, the command completed execution.
        ///
        /// NOW-PROTO: NOW_EXEC_FLAG_DATA_LAST
        /// </summary>
        public bool Last => _flags.HasFlag(MsgFlags.Last);
        public uint SessionId { get; private init; }
        public ArraySegment<byte> Data { get; private init; }

        private readonly MsgFlags _flags;
    }
}