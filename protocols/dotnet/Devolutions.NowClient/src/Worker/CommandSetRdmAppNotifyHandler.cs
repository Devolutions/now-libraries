namespace Devolutions.NowClient.Worker
{
    internal class CommandSetRdmAppNotifyHandler : IClientCommand
    {
        private readonly RdmAppNotifyHandler? _handler;

        public CommandSetRdmAppNotifyHandler(RdmAppNotifyHandler? handler)
        {
            _handler = handler;
        }

        Task IClientCommand.Execute(WorkerCtx ctx)
        {
            ctx.RdmAppNotifyHandler = _handler;
            return Task.CompletedTask;
        }
    }
}