using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandExecCancel(uint sessionId) : IClientCommand
    {
        Task IClientCommand.Execute(WorkerCtx ctx)
        {
            // This only sends the request to cancel the session, the session state
            // will be only changed when the response is received.
            return ctx.NowChannel.WriteMessage(new NowMsgExecCancelReq(sessionId));
        }
    }
}