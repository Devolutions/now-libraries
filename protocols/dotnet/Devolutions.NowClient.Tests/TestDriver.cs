using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Devolutions.NowClient.Tests;

internal static class TestDriver
{
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    public static void Run(Action action, int timeoutSeconds = 60)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            IsBackground = true,
            Name = "NOW integration test driver",
        };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(timeoutSeconds)), "Raw test driver exceeded its deadline.");
        failure?.Throw();
    }

    // Task.Wait, WaitAsync and Task.Delay can schedule pool work (including
    // blocking compensation). Observe completion from a native driver instead.
    public static void Until(Func<bool> condition, string description, TimeSpan? timeout = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < (timeout ?? Deadline), $"Timed out: {description}");
            Thread.Sleep(1);
        }
    }

    public static void Completed(Task task, TimeSpan? timeout = null)
    {
        Until(() => task.IsCompleted, "task completion", timeout);
    }

    public static void Success(Task task, TimeSpan? timeout = null)
    {
        Completed(task, timeout);
        task.GetAwaiter().GetResult();
    }

    public static T Success<T>(Task<T> task, TimeSpan? timeout = null)
    {
        Success((Task)task, timeout);
        return task.GetAwaiter().GetResult();
    }

    public static Exception Failure(Task task, TimeSpan? timeout = null)
    {
        Completed(task, timeout);
        return Assert.IsAssignableFrom<Exception>(Record.Exception(() => task.GetAwaiter().GetResult()));
    }

    public static void ThreadExited(Thread thread)
    {
        Assert.True(thread.Join(Deadline), $"Protocol thread {thread.ManagedThreadId} did not exit.");
    }
}

internal sealed class FrozenUiContext : SynchronizationContext
{
    private int _posts;
    public int Posts => Volatile.Read(ref _posts);

    public override void Post(SendOrPostCallback d, object? state)
    {
        Interlocked.Increment(ref _posts);
        // Deliberately never pump. Internal client continuations must not need
        // the caller's UI thread, even if the application is busy.
    }

    public Task Invoke(Func<Task> action)
    {
        var previous = Current;
        try
        {
            SetSynchronizationContext(this);
            return action();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    public Task<T> Invoke<T>(Func<Task<T>> action) => (Task<T>)Invoke(() => (Task)action());
}