using Devolutions.NowClient.Execution;

namespace Devolutions.NowClient
{
    internal static class ProtocolTasks
    {
        // Task.WhenAny/WaitAsync can schedule their internal completion actions on the pool
        // for a RunContinuationsAsynchronously source. Capture first so the race is completed
        // on the protocol context, not by the transport's task scheduler.
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
            finally
            {
                deadline.Cancel();
            }
        }
    }
}