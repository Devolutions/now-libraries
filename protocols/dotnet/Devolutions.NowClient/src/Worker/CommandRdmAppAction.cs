using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandRdmAppAction : IClientCommand
    {
        private readonly NowMsgRdmAppAction _message;

        public CommandRdmAppAction(NowMsgRdmAppAction message)
        {
            _message = message;
        }

        public async Task Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(_message);
        }
    }
}