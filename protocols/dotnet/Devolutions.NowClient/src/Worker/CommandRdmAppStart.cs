using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandRdmAppStart : IClientCommand
    {
        private readonly NowMsgRdmAppStart _message;

        public CommandRdmAppStart(NowMsgRdmAppStart message)
        {
            _message = message;
        }

        public async Task Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(_message);
        }
    }
}