using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandRdmSessionAction : IClientCommand
    {
        private readonly NowMsgRdmSessionAction _message;

        public CommandRdmSessionAction(NowMsgRdmSessionAction message)
        {
            _message = message;
        }

        public async Task Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(_message);
        }
    }
}