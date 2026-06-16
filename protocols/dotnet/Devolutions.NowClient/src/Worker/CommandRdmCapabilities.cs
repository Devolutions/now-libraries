using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    internal class CommandRdmCapabilities : IClientCommand
    {
        private readonly NowMsgRdmCapabilities _message;
        private readonly RdmCapabilitiesResponseHandler _responseHandler;

        public CommandRdmCapabilities(NowMsgRdmCapabilities message, RdmCapabilitiesResponseHandler responseHandler)
        {
            _message = message;
            _responseHandler = responseHandler;
        }

        public async Task Execute(WorkerCtx ctx)
        {
            await ctx.NowChannel.WriteMessage(_message);

            // Register the response handler
            ctx.RegisterRdmCapabilitiesHandler(_responseHandler);
        }
    }
}