using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandSessionWindowRecStart(NowMsgSessionWindowRecStart message) : IClientCommand
    {
        public async Task Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(message);
        }
    }
}