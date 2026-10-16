using Devolutions.NowClient.Execution;
using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    /// <summary>
    /// Response handler for RDM capabilities messages.
    /// </summary>
    internal class RdmCapabilitiesResponseHandler : IDisposable
    {
        private readonly TaskCompletionSource<NowMsgRdmCapabilities> _response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SetResponse(NowMsgRdmCapabilities response)
        {
            _response.TrySetResult(response);
        }

        public void SetException(Exception exception)
        {
            _response.TrySetException(exception);
        }

        public Task<NowMsgRdmCapabilities> WaitForResponseAsync(
            TimeSpan timeout, DedicatedExecution? execution, CancellationToken cancellationToken)
        {
            return ProtocolTasks.WaitAsync(_response.Task, timeout, execution, cancellationToken);
        }

        public void Dispose()
        {
            _response.TrySetCanceled();
        }
    }
}