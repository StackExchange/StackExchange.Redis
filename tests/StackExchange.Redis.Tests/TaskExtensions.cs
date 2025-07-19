#if !NET
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests;

internal static class TaskExtensions
{
    // suboptimal polyfill version of the .NET 6+ API; I'm not recommending this for production use,
    // but it's good enough for tests
    public static Task<T> WaitAsync<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        if (task.IsCompleted || !cancellationToken.CanBeCanceled) return task;
        return Wrap(task, cancellationToken);

        static async Task<T> Wrap(Task<T> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<T>();
            using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            _ = task.ContinueWith(t =>
            {
                if (t.IsCanceled) tcs.TrySetCanceled();
                else if (t.IsFaulted) tcs.TrySetException(t.Exception!);
                else tcs.TrySetResult(t.Result);
            });
            return await tcs.Task;
        }
    }

    public static Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout)
    {
        if (task.IsCompleted) return task;
        return Wrap(task, timeout);

        static async Task<T> Wrap(Task<T> task, TimeSpan timeout)
        {
            Task other = Task.Delay(timeout);
            var first = await Task.WhenAny(task, other);
            if (ReferenceEquals(first, other))
            {
                throw new TimeoutException();
            }
            return await task;
        }
    }
}

#endif
