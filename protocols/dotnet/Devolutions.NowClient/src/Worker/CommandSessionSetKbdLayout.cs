using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandSessionSetKbdLayout(NowMsgSessionSetKbdLayout request) : IClientCommand
    {
        public async Task Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(request);
        }
    }
}