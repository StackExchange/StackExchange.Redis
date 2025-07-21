using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal static class TaskExtensions
    {
        private static readonly Action<Task> observeErrors = ObverveErrors;
        private static void ObverveErrors(this Task task)
        {
            if (task != null) GC.KeepAlive(task.Exception);
        }

        internal static Task ObserveErrors(this Task task)
        {
            task.ContinueWith(observeErrors, TaskContinuationOptions.OnlyOnFaulted);
            return task;
        }

        internal static Task<T> ObserveErrors<T>(this Task<T> task)
        {
            task.ContinueWith(observeErrors, TaskContinuationOptions.OnlyOnFaulted);
            return task;
        }

#if !NET6_0_OR_GREATER
        // suboptimal polyfill version of the .NET 6+ API, but reasonable for light use
        internal static Task<T> WaitAsync<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            if (task.IsCompleted || !cancellationToken.CanBeCanceled) return task;
            return Wrap(task, cancellationToken);

            static async Task<T> Wrap(Task<T> task, CancellationToken cancellationToken)
            {
                var tcs = new TaskSourceWithToken<T>(cancellationToken);
                using var reg = cancellationToken.Register(
                    static state => ((TaskSourceWithToken<T>)state!).Cancel(), tcs);
                _ = task.ContinueWith(
                    static (t, state) =>
                    {
                        var tcs = (TaskSourceWithToken<T>)state!;
                        if (t.IsCanceled) tcs.TrySetCanceled();
                        else if (t.IsFaulted) tcs.TrySetException(t.Exception!);
                        else tcs.TrySetResult(t.Result);
                    },
                    tcs);
                return await tcs.Task;
            }
        }

        // the point of this type is to combine TCS and CT so that we can use a static
        // registration via Register
        private sealed class TaskSourceWithToken<T> : TaskCompletionSource<T>
        {
            public TaskSourceWithToken(CancellationToken cancellationToken)
                => _cancellationToken = cancellationToken;

            private readonly CancellationToken _cancellationToken;

            public void Cancel() => TrySetCanceled(_cancellationToken);
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ConfiguredTaskAwaitable ForAwait(this Task task) => task.ConfigureAwait(false);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ConfiguredValueTaskAwaitable ForAwait(this in ValueTask task) => task.ConfigureAwait(false);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ConfiguredTaskAwaitable<T> ForAwait<T>(this Task<T> task) => task.ConfigureAwait(false);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ConfiguredValueTaskAwaitable<T> ForAwait<T>(this in ValueTask<T> task) => task.ConfigureAwait(false);

        internal static void RedisFireAndForget(this Task task) => task?.ContinueWith(static t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);

        /// <summary>
        /// Licensed to the .NET Foundation under one or more agreements.
        /// The .NET Foundation licenses this file to you under the MIT license.
        /// </summary>
        /// <remarks>Inspired from <see href="https://github.com/dotnet/corefx/blob/81a246f3adf1eece3d981f1d8bb8ae9de12de9c6/src/Common/tests/System/Threading/Tasks/TaskTimeoutExtensions.cs#L15-L43"/>.</remarks>
        internal static async Task<bool> TimeoutAfter(this Task task, int timeoutMs)
        {
            var cts = new CancellationTokenSource();
            if (task == await Task.WhenAny(task, Task.Delay(timeoutMs, cts.Token)).ForAwait())
            {
                cts.Cancel();
                await task.ForAwait();
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
