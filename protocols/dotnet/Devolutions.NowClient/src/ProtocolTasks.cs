using Devolutions.NowClient.Execution;

namespace Devolutions.NowClient
{
    internal static class ProtocolTasks
    {
        // Capturing avoids pool-dispatched WhenAny/WaitAsync completion actions on asynchronous transport tasks.
        private static async Task<T> Capture<T>(Task<T> task) => await task;

        private static async Task<bool> Capture(Task task)
        {
            await task;
            return true;
        }

        public static Task WaitAsync(Task task, DedicatedExecution? execution, CancellationToken cancellationToken)
        {
            return WaitAsync(Capture(task), Timeout.InfiniteTimeSpan, execution, cancellationToken);
        }

        public static Task<T> WaitAsync<T>(Task<T> task, DedicatedExecution? execution, CancellationToken cancellationToken)
        {
            return WaitAsync(task, Timeout.InfiniteTimeSpan, execution, cancellationToken);
        }

        public static async Task<T> WaitAsync<T>(
            Task<T> task,
            TimeSpan timeout,
            DedicatedExecution? execution,
            CancellationToken cancellationToken)
        {
            if (execution == null)
            {
                try
                {
                    return await task.WaitAsync(timeout, cancellationToken);
                }
                catch
                {
                    ObserveFault(task);
                    throw;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (task.IsCompleted)
            {
                return await task;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var captured = Capture(task);
            var delay = execution.Delay(timeout, deadline.Token);
            try
            {
                var completed = await Task.WhenAny(captured, delay);
                if (completed == captured)
                {
                    return await captured;
                }

                await delay;
                throw new TimeoutException();
            }
            catch
            {
                ObserveFault(captured);
                throw;
            }
            finally
            {
                deadline.Cancel();
            }
        }

        // A wait abandoned by timeout or cancellation leaves the transport task to fault later, typically on disposal.
        private static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}