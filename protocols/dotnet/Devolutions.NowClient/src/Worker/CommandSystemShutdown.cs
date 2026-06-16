using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandSystemShutdown(NowMsgSystemShutdown request) : IClientCommand
    {
        public async Task Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(_request);
        }

        private readonly NowMsgSystemShutdown _request = request;
    }
}