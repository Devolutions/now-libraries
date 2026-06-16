namespace Devolutions.NowClient.Worker
{
    internal interface IClientCommand
    {
        Task Execute(WorkerCtx ctx);
    }
}