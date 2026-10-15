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

        public static async Task WaitAsync(Task task, DedicatedExecution? execution, CancellationToken cancellationToken)
        {
            try
            {
                await WaitAsync(Capture(task), Timeout.InfiniteTimeSpan, execution, cancellationToken);
            }
            catch
            {
                // The inner wait only sees the wrapper; the transport task underneath is the one that faults late.
                ObserveFault(task);
                throw;
            }
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
            try
            {
                if (execution == null)
                {
                    return await task.WaitAsync(timeout, cancellationToken);
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
                    // The capture resumes on the pump; once the pump has closed it never runs, so observing it alone
                    // would leave the transport's own fault unobserved.
                    ObserveFault(captured);
                    throw;
                }
                finally
                {
                    deadline.Cancel();
                }
            }
            catch
            {
                ObserveFault(task);
                throw;
            }
        }

        // A wait abandoned by timeout, cancellation or shutdown leaves its task to fault later, typically on disposal.
        // Observation may use the pool: nothing on the protocol path waits for it.
        public static void ObserveFault(Task task)
        {
            if (task.IsCompleted)
            {
                _ = task.Exception;
                return;
            }

            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}