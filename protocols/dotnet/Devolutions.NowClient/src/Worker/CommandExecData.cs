using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandExecData(
        uint sessionId,
        ArraySegment<byte> data,
        bool last
    ) : IClientCommand
    {
        private const int DataChunkSize = 1024 * 32; // 32K chunks

        async Task IClientCommand.Execute(WorkerCtx ctx)
        {
            int offset = 0;
            // Send data in chunks if provided input data is too large.
            while (offset < data.Count)
            {
                int length = Math.Min(DataChunkSize, data.Count - offset);

                await ctx.NowChannel.WriteMessage(new NowMsgExecData(sessionId, NowMsgExecData.StreamKind.Stdin, last,
                    data[offset..(offset + length)]));
                offset += length;
            }
        }
    }
}