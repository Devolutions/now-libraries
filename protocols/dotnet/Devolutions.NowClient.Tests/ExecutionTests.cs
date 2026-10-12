using System.Collections.Concurrent;
using System.Diagnostics;

namespace Devolutions.NowClient.Tests;

public sealed class ExecutionTests
{
    [Fact]
    public void ConstructorStartsPumpAndRunDoesNotCaptureFrozenUiContext()
    {
        TestDriver.Run(() =>
        {
            var execution = new NowClientExecution();
            var external = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = 0;
            Thread? worker = null;
            SynchronizationContext? context = null;
            var ui = new FrozenUiContext();
            try
            {
                var result = ui.Invoke(() => execution.Run(async () =>
                {
                    worker = Thread.CurrentThread;
                    context = SynchronizationContext.Current;
                    Volatile.Write(ref entered, 1);
                    var value = await external.Task;
                    Assert.Same(worker, Thread.CurrentThread);
                    Assert.Same(context, SynchronizationContext.Current);
                    await execution.Delay(TimeSpan.FromMilliseconds(20));
                    Assert.Same(worker, Thread.CurrentThread);
                    return value;
                }));
                TestDriver.Until(() => Volatile.Read(ref entered) == 1, "Run delegate on new pump");
                // A second turn proves the first operation suspended before native completion.
                TestDriver.Success(ui.Invoke(() => execution.Run(() => Task.CompletedTask)));
                Assert.False(result.IsCompleted);
                Assert.NotNull(context);
                Assert.NotNull(worker);
                Assert.True(worker.IsBackground);
                Assert.False(worker.IsThreadPoolThread);
                Assert.NotEqual(Thread.CurrentThread, worker);
                external.SetResult(true);
                Assert.True(TestDriver.Success(result));
                Assert.Equal(0, ui.Posts);
                Assert.False(execution.Completion.IsCompleted);
            }
            finally
            {
                external.TrySetResult(true);
                TestDriver.Success(execution.DisposeAsync().AsTask());
            }
            TestDriver.Success(execution.Completion);
            TestDriver.ThreadExited(worker!);
        });
    }

    [Fact]
    public void ConcurrentAndNestedRunsKeepTheirResultsAndOneExecutionThread()
    {
        TestDriver.Run(() =>
        {
            var execution = new NowClientExecution();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = new ConcurrentQueue<(Thread Thread, SynchronizationContext? Context)>();
            var tasks = new Task<int>[24];
            var entered = 0;
            var submitters = Enumerable.Range(0, 4).Select(index => new Thread(() =>
            {
                for (var offset = index; offset < tasks.Length; offset += 4)
                {
                    var value = offset;
                    tasks[value] = execution.Run(async () =>
                    {
                        calls.Enqueue((Thread.CurrentThread, SynchronizationContext.Current));
                        Interlocked.Increment(ref entered);
                        await release.Task;
                        return await execution.Run(async () =>
                        {
                            await execution.Delay(TimeSpan.FromMilliseconds(5 + value));
                            calls.Enqueue((Thread.CurrentThread, SynchronizationContext.Current));
                            return value * 2;
                        });
                    });
                }
            })
            { IsBackground = true }).ToArray();
            try
            {
                foreach (var submitter in submitters)
                    submitter.Start();
                foreach (var submitter in submitters)
                    TestDriver.ThreadExited(submitter);
                TestDriver.Until(() => Volatile.Read(ref entered) == tasks.Length, "all concurrent Runs suspended");
                Assert.All(tasks, task => Assert.False(task.IsCompleted));
                release.SetResult();
                for (var index = 0; index < tasks.Length; index++)
                    Assert.Equal(index * 2, TestDriver.Success(tasks[index]));
                var first = calls.First();
                Assert.NotNull(first.Context);
                Assert.False(first.Thread.IsThreadPoolThread);
                Assert.All(calls, call =>
                {
                    Assert.Same(first.Thread, call.Thread);
                    Assert.Same(first.Context, call.Context);
                });
                Assert.False(execution.Completion.IsCompleted);
            }
            finally
            {
                release.TrySetResult();
                TestDriver.Success(execution.DisposeAsync().AsTask());
            }
            TestDriver.ThreadExited(calls.First().Thread);
        });
    }

    [Fact]
    public void RunFailuresAndCancellationDoNotPoisonThePump()
    {
        TestDriver.Run(() =>
        {
            var execution = new NowClientExecution();
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var fault = new IOException("external operation failed");
            try
            {
                Assert.Same(fault, TestDriver.Failure(execution.Run((Func<Task>)(() => throw fault))));
                Assert.Same(fault, TestDriver.Failure(execution.Run<int>(() => throw fault)));
                Assert.Same(fault, TestDriver.Failure(execution.Run(async () =>
                {
                    await execution.Delay(TimeSpan.FromMilliseconds(10));
                    throw fault;
                })));
                var cancellation = execution.Run(() => Task.FromCanceled<int>(canceled.Token));
                var error = Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(cancellation));
                Assert.Equal(canceled.Token, error.CancellationToken);
                Assert.True(cancellation.IsCanceled);
                Assert.Equal(42, TestDriver.Success(execution.Run(() => Task.FromResult(42))));
                Assert.False(execution.Completion.IsCompleted);
            }
            finally
            {
                TestDriver.Success(execution.DisposeAsync().AsTask());
            }
        });
    }

    [Fact]
    public void DelaysHonorDeadlinesCancellationAndInfiniteDurations()
    {
        TestDriver.Run(() =>
        {
            var execution = new NowClientExecution();
            using var canceled = new CancellationTokenSource();
            using var infiniteStop = new CancellationTokenSource();
            using var maximumStop = new CancellationTokenSource();
            using var finiteStop = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                TestDriver.Success(execution.Delay(TimeSpan.Zero));
                Assert.IsType<ArgumentOutOfRangeException>(
                    TestDriver.Failure(execution.Delay(TimeSpan.FromMilliseconds(-2))));
                var preCanceled = execution.Delay(TimeSpan.Zero, canceled.Token);
                Assert.Equal(canceled.Token,
                    Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(preCanceled)).CancellationToken);
                Assert.True(preCanceled.IsCanceled);

                var infinite = execution.Delay(Timeout.InfiniteTimeSpan, infiniteStop.Token);
                var maximum = execution.Delay(TimeSpan.MaxValue, maximumStop.Token);
                var finite = execution.Delay(TimeSpan.FromSeconds(30), finiteStop.Token);
                var elapsed = Stopwatch.StartNew();
                var later = execution.Delay(TimeSpan.FromMilliseconds(300));
                var earlier = execution.Delay(TimeSpan.FromMilliseconds(80));
                TestDriver.Success(earlier);
                Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(70));
                Assert.False(infinite.IsCompleted);
                Assert.False(maximum.IsCompleted);
                Assert.False(finite.IsCompleted);
                infiniteStop.Cancel();
                maximumStop.Cancel();
                finiteStop.Cancel();
                Assert.Equal(infiniteStop.Token,
                    Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(infinite)).CancellationToken);
                Assert.Equal(maximumStop.Token,
                    Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(maximum)).CancellationToken);
                Assert.Equal(finiteStop.Token,
                    Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(finite)).CancellationToken);
                TestDriver.Success(later);
                Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(280));
                TestDriver.Success(execution.Run(() => execution.Delay(TimeSpan.FromMilliseconds(10))));
            }
            finally
            {
                infiniteStop.Cancel();
                maximumStop.Cancel();
                finiteStop.Cancel();
                TestDriver.Success(execution.DisposeAsync().AsTask());
            }
        });
    }

    [Fact]
    public void DisposeWaitsForAdmittedOperationsAndDelaysAndRejectsNewWork()
    {
        TestDriver.Run(() =>
        {
            var execution = new NowClientExecution();
            var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var delayStop = new CancellationTokenSource();
            var entered = 0;
            Thread? worker = null;
            try
            {
                var admitted = execution.Run(async () =>
                {
                    worker = Thread.CurrentThread;
                    Volatile.Write(ref entered, 1);
                    var value = await release.Task;
                    Assert.Same(worker, Thread.CurrentThread);
                    await Task.Yield();
                    Assert.Same(worker, Thread.CurrentThread);
                    return value;
                });
                var delay = execution.Delay(Timeout.InfiniteTimeSpan, delayStop.Token);
                TestDriver.Until(() => Volatile.Read(ref entered) == 1, "admitted operation awaiting external task");
                TestDriver.Success(execution.Run(() => Task.CompletedTask));
                var disposal = execution.DisposeAsync().AsTask();
                var repeatedDisposal = ((IAsyncDisposable)execution).DisposeAsync().AsTask();
                Assert.False(disposal.IsCompleted);
                Assert.False(repeatedDisposal.IsCompleted);
                Assert.False(execution.Completion.IsCompleted);
                var invoked = false;
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    _ = execution.Run(() =>
                    {
                        invoked = true;
                        return Task.CompletedTask;
                    });
                });
                Assert.Throws<ObjectDisposedException>(() => { _ = execution.Run(() => Task.FromResult(true)); });
                Assert.Throws<ObjectDisposedException>(() => { _ = execution.Delay(TimeSpan.Zero); });
                Assert.False(invoked);
                release.SetResult(73);
                Assert.Equal(73, TestDriver.Success(admitted));
                Assert.False(disposal.IsCompleted);
                Assert.False(repeatedDisposal.IsCompleted);
                Assert.False(delay.IsCompleted);
                delayStop.Cancel();
                Assert.IsAssignableFrom<OperationCanceledException>(TestDriver.Failure(delay));
                TestDriver.Success(disposal);
                TestDriver.Success(repeatedDisposal);
                TestDriver.Success(execution.Completion);
                TestDriver.ThreadExited(worker!);
                Assert.Throws<ObjectDisposedException>(() => { _ = execution.Run(() => Task.CompletedTask); });
            }
            finally
            {
                release.TrySetResult(73);
                delayStop.Cancel();
                TestDriver.Success(execution.DisposeAsync().AsTask());
            }
        });
    }

    [Fact]
    public void DisposalCanStartInsideOperationWithoutAwaitingIt()
    {
        TestDriver.Run(() =>
        {
            var execution = new NowClientExecution();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var first = execution.Run(() =>
                {
                    var nested = execution.Run(async () => await release.Task);
                    var disposal = execution.DisposeAsync().AsTask();
                    Assert.False(disposal.IsCompleted);
                    return Task.FromResult((Nested: nested, Disposal: disposal));
                });
                var result = TestDriver.Success(first);
                Assert.False(result.Disposal.IsCompleted);
                release.SetResult(true);
                Assert.True(TestDriver.Success(result.Nested));
                TestDriver.Success(result.Disposal);
                TestDriver.Success(execution.Completion);
            }
            finally
            {
                release.TrySetResult(true);
                TestDriver.Success(execution.DisposeAsync().AsTask());
            }
        });
    }
}