using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class AwaitableMutexTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void IsolatedSyncSuccessAndReturn()
    {
        using var mutex = AwaitableMutex.Create(timeoutMilliseconds: 100);

        for (var i = 0; i < 5; i++)
        {
            Assert.True(mutex.IsAvailable);
            Assert.True(i % 2 == 0 ? mutex.TryTakeInstant() : mutex.TryTakeSync());
            Assert.False(mutex.IsAvailable);
            mutex.Release();
        }

        Assert.True(mutex.IsAvailable);
    }

    [Fact]
    public async Task SyncCallerTimesOutWhileHeld()
    {
        using var mutex = AwaitableMutex.Create(timeoutMilliseconds: 50);
        Assert.True(mutex.TryTakeInstant());

        var result = await WithTimeout(Task.Run(() => mutex.TryTakeSync()));

        Assert.False(result);
        Assert.False(mutex.IsAvailable);
        mutex.Release();
        Assert.True(mutex.IsAvailable);
    }

    [Fact]
    public async Task AsyncCallerTimesOutWhileHeld()
    {
        using var mutex = AwaitableMutex.Create(timeoutMilliseconds: 50);
        Assert.True(mutex.TryTakeInstant());

        var result = await WithTimeout(mutex.TryTakeAsync().AsTask());

        Assert.False(result);
        Assert.False(mutex.IsAvailable);
        mutex.Release();
        Assert.True(mutex.IsAvailable);
    }

    [Fact]
    public void DisposalPreventsNewAcquisitions()
    {
        var mutex = AwaitableMutex.Create(timeoutMilliseconds: 100);
        Assert.True(mutex.TryTakeInstant());

        mutex.Dispose();

        Assert.False(mutex.IsAvailable);
        Assert.Throws<ObjectDisposedException>(() => mutex.TryTakeInstant());
        Assert.Throws<ObjectDisposedException>(() => mutex.TryTakeSync());
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = mutex.TryTakeAsync();
        });
        Assert.Throws<ObjectDisposedException>(() => mutex.Release());
    }

    [Fact]
    public async Task MixedSyncAndAsyncWaitersAreReleased()
    {
        const int Iterations = 100;
        using var mutex = AwaitableMutex.Create(timeoutMilliseconds: 10_000);

        for (var i = 0; i < Iterations; i++)
        {
            await Core(i, mutex);
        }

        static async Task Core(int iteration, AwaitableMutex mutex)
        {
            Assert.True(mutex.TryTakeInstant());

            var order = new List<string>();
            var expected = new[]
            {
                $"{iteration}:sync-1",
                $"{iteration}:async-1",
                $"{iteration}:sync-2",
                $"{iteration}:async-2",
            };

            var sync1 = StartSyncWaiter(mutex, expected[0], order, out var sync1Thread);
            WaitForBlocked(sync1Thread);

            var async1 = StartAsyncWaiter(mutex, expected[1], order);
            Assert.False(async1.IsCompleted);

            var sync2 = StartSyncWaiter(mutex, expected[2], order, out var sync2Thread);
            WaitForBlocked(sync2Thread);

            var async2 = StartAsyncWaiter(mutex, expected[3], order);
            Assert.False(async2.IsCompleted);

            mutex.Release();

            await WithTimeout(Task.WhenAll(sync1, async1, sync2, async2));

            // SemaphoreSlim does not guarantee FIFO ordering; this only verifies that every queued waiter arrives.
            order.Sort(StringComparer.Ordinal);
            Array.Sort(expected, StringComparer.Ordinal);
            Assert.Equal(expected, order);
            Assert.True(mutex.IsAvailable);
        }
    }

    private static Task StartSyncWaiter(AwaitableMutex mutex, string name, List<string> order, out Thread thread)
    {
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            started.Set();
            try
            {
                if (!mutex.TryTakeSync()) throw new TimeoutException();

                Add(order, name);
                mutex.Release();
                source.TrySetResult(true);
            }
            catch (Exception ex)
            {
                source.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = name,
        };
        thread.Start();

        Assert.True(started.Wait(TestTimeout), $"{name} did not start");
        return source.Task;
    }

    private static async Task StartAsyncWaiter(AwaitableMutex mutex, string name, List<string> order)
    {
        if (!await mutex.TryTakeAsync().AsTask()) throw new TimeoutException();

        Add(order, name);
        mutex.Release();
    }

    private static void WaitForBlocked(Thread thread)
    {
        Assert.True(
            SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, TestTimeout),
            $"{thread.Name} did not block");
    }

    private static void Add(List<string> order, string name)
    {
        lock (order)
        {
            order.Add(name);
        }
    }

    private static async Task WithTimeout(Task task)
    {
        var timeout = Task.Delay(TestTimeout);
        var first = await Task.WhenAny(task, timeout);
        Assert.Same(task, first);
        await task;
    }

    private static async Task<T> WithTimeout<T>(Task<T> task)
    {
        var timeout = Task.Delay(TestTimeout);
        var first = await Task.WhenAny(task, timeout);
        Assert.Same(task, first);
        return await task;
    }
}
