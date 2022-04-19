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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ConfiguredTaskAwaitable ForAwait(this Task task) => task.ConfigureAwait(false);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ConfiguredValueTaskAwaitable ForAwait(this in ValueTask task) => task.ConfigureAwait(false);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ConfiguredTaskAwaitable<T> ForAwait<T>(this Task<T> task) => task.ConfigureAwait(false);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ConfiguredValueTaskAwaitable<T> ForAwait<T>(this in ValueTask<T> task) => task.ConfigureAwait(false);

        internal static void RedisFireAndForget(this Task task) => task?.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);

        /// <summary>
        /// Licensed to the .NET Foundation under one or more agreements.
        /// The .NET Foundation licenses this file to you under the MIT license.
        /// </summary>
        /// <remarks>Inspired from <see href="https://github.com/dotnet/corefx/blob/81a246f3adf1eece3d981f1d8bb8ae9de12de9c6/src/Common/tests/System/Threading/Tasks/TaskTimeoutExtensions.cs#L15-L43"/></remarks>
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
