using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandSessionMsgBox(NowMsgSessionMessageBoxReq request, IMessageBoxRspHandler? handler)
        : IClientCommand
    {
        public async Task Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(request);

            if (handler != null)
            {
                ctx.MessageBoxHandlers[request.RequestId] = handler;
            }
        }
    }
}