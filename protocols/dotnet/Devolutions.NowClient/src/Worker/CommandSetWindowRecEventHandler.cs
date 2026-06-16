namespace Devolutions.NowClient.Worker
{
    internal class CommandSetWindowRecEventHandler : IClientCommand
    {
        private readonly IWindowRecEventHandler? _handler;

        public CommandSetWindowRecEventHandler(IWindowRecEventHandler? handler)
        {
            _handler = handler;
        }

        async Task IClientCommand.Execute(WorkerCtx ctx)
        {
            ctx.WindowRecEventHandler = _handler;
            await Task.CompletedTask;
        }
    }
}