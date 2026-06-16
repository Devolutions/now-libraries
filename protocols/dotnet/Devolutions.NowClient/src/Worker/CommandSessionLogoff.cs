using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandSessionLogoff : IClientCommand
    {
        public async Task Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(new NowMsgSessionLogoff());
        }
    }
}