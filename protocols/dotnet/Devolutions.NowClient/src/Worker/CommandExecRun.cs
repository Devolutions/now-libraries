using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandExecRun(NowMsgExecRun message) : IClientCommand
    {
        async Task IClientCommand.Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(message);
            // No handler is required for fire-and-forget execution.
        }
    }
}