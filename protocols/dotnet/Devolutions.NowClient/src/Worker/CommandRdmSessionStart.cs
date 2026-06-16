using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandRdmSessionStart : IClientCommand
    {
        private readonly NowMsgRdmSessionStart _message;

        public CommandRdmSessionStart(NowMsgRdmSessionStart message)
        {
            _message = message;
        }

        public async Task Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(_message);
        }
    }
}