using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandChannelClose : IClientCommand
    {
        async Task IClientCommand.Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(NowMsgChannelClose.Graceful());

            // Exit worker and block any further command execution.
            ctx.ExitRequested = true;
        }
    }
}