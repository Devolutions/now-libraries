using Devolutions.NowProto.Messages;

namespace Devolutions.NowClient.Worker
{
    /// <summary>
    /// Response handler for RDM capabilities messages.
    /// </summary>
    internal class RdmCapabilitiesResponseHandler : IDisposable
    {
        private readonly SemaphoreSlim _responseReceived = new(0, 1);
        private NowMsgRdmCapabilities? _response;
        private Exception? _exception;
        private volatile bool _isCompleted;
        private bool _disposed;

        public void SetResponse(NowMsgRdmCapabilities response)
        {
            if (_disposed || _isCompleted)
                return;

            _response = response;
            _isCompleted = true;
            _responseReceived.Release();
        }

        public void SetException(Exception exception)
        {
            if (_disposed || _isCompleted)
                return;

            _exception = exception;
            _isCompleted = true;
            _responseReceived.Release();
        }

        public async Task<NowMsgRdmCapabilities> WaitForResponseAsync(TimeSpan timeout)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RdmCapabilitiesResponseHandler));

            if (!await _responseReceived.WaitAsync(timeout))
            {
                throw new TimeoutException("RDM capabilities response timeout");
            }

            if (_exception != null)
                throw _exception;

            return _response ?? throw new InvalidOperationException("Response was not set");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _responseReceived?.Dispose();
                _disposed = true;
            }
        }
    }
}