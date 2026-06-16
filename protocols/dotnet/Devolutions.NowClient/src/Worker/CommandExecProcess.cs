using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandExecProcess(NowMsgExecProcess message, IExecSessionHandler handler) : IClientCommand
    {
        async Task IClientCommand.Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(message);

            // Register the handler for the session to receive
            // the result and task output.
            ctx.ExecSessionHandlers[message.SessionId] = handler;
        }
    }
}