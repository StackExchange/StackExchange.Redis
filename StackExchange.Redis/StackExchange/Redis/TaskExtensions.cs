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

        public static Task ObserveErrors(this Task task)
        {
            task?.ContinueWith(observeErrors, TaskContinuationOptions.OnlyOnFaulted);
            return task;
        }

        public static Task<T> ObserveErrors<T>(this Task<T> task)
        {
            task?.ContinueWith(observeErrors, TaskContinuationOptions.OnlyOnFaulted);
            return task;
        }

        public static ConfiguredTaskAwaitable ForAwait(this Task task) => task.ConfigureAwait(false);
        public static ConfiguredTaskAwaitable<T> ForAwait<T>(this Task<T> task) => task.ConfigureAwait(false);
        public static ConfiguredValueTaskAwaitable<T> ForAwait<T>(this ValueTask<T> task) => task.ConfigureAwait(false);

        // Inspired from https://github.com/dotnet/corefx/blob/81a246f3adf1eece3d981f1d8bb8ae9de12de9c6/src/Common/tests/System/Threading/Tasks/TaskTimeoutExtensions.cs#L15-L43
        // Licensed to the .NET Foundation under one or more agreements.
        // The .NET Foundation licenses this file to you under the MIT license.
        public static async Task TimeoutAfter(this Task task, int millisecondsTimeout, string message)
        {
            var cts = new CancellationTokenSource();
            if (task == await Task.WhenAny(task, Task.Delay(millisecondsTimeout, cts.Token)).ConfigureAwait(false))
            {
                cts.Cancel();
                await task.ConfigureAwait(false);
            }
            else
            {
                throw new TimeoutException(message);
            }
        }

        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int millisecondsTimeout, string message)
        {
            var cts = new CancellationTokenSource();
            if (task == await Task<TResult>.WhenAny(task, Task<TResult>.Delay(millisecondsTimeout, cts.Token)).ConfigureAwait(false))
            {
                cts.Cancel();
                return await task.ConfigureAwait(false);
            }
            else
            {
                throw new TimeoutException(message);
            }
        }
    }
}
