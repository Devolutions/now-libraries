using System.Text;

using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandExecAbort(uint sessionId, uint exitCode) : IClientCommand
    {
        async Task IClientCommand.Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(new NowMsgExecAbort(sessionId, exitCode));

            // Abort unregisters exec session unconditionally if the message
            // was successfully sent.
            ctx.ExecSessionHandlers.Remove(sessionId);
        }
    }
}