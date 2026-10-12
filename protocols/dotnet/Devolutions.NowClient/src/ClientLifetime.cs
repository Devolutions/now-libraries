using Devolutions.NowClient.Execution;

namespace Devolutions.NowClient
{
    internal sealed class ClientLifetime : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly CancellationToken _token;
        private readonly DedicatedExecution? _execution;
        private readonly CancellationTokenRegistration _registration;
        private bool _disposed;

        public ClientLifetime(DedicatedExecution? execution, CancellationToken cancellationToken)
        {
            _token = _stop.Token;
            _execution = execution;
            _registration = cancellationToken.Register(RequestStop);
        }

        public CancellationToken Token => _token;

        public void RequestStop()
        {
            if (_execution != null)
            {
                _execution.TryPost(Cancel);
            }
            else
            {
                Cancel();
            }
        }

        private void Cancel()
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _stop.Cancel();
                }
            }
        }

        public void Dispose()
        {
            // Unregister outside the gate: an in-flight cancellation may need that gate.
            _registration.Dispose();
            lock (_gate)
            {
                if (_disposed)
                    return;
                try
                {
                    _stop.Cancel();
                }
                finally
                {
                    _disposed = true;
                    _stop.Dispose();
                }
            }
        }
    }
}