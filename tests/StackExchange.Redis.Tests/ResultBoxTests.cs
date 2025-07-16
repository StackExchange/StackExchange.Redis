using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ResultBoxTests
{
    [Fact]
    public void SyncResultBox()
    {
        var msg = Message.Create(-1, CommandFlags.None, RedisCommand.PING, CancellationToken.None);
        var box = SimpleResultBox<string>.Get(msg.CancellationToken);
        Assert.False(box.IsAsync);

        int activated = 0;
        lock (box)
        {
            Task.Run(() =>
            {
                lock (box)
                {
                    // release the worker to start work
                    Monitor.PulseAll(box);

                    // wait for the completion signal
                    if (Monitor.Wait(box, TimeSpan.FromSeconds(10)))
                    {
                        Interlocked.Increment(ref activated);
                    }
                }
            });
            Assert.True(Monitor.Wait(box, TimeSpan.FromSeconds(10)), "failed to handover lock to worker");
        }

        // check that continuation was not already signalled
        Thread.Sleep(100);
        Assert.Equal(0, Volatile.Read(ref activated));

        msg.SetSource(ResultProcessor.DemandOK, box);
        Assert.True(msg.TrySetResult("abc"));

        // check that TrySetResult did not signal continuation
        Thread.Sleep(100);
        Assert.Equal(0, Volatile.Read(ref activated));

        // check that complete signals continuation
        msg.Complete();
        Thread.Sleep(100);
        Assert.Equal(1, Volatile.Read(ref activated));

        var s = box.GetResult(out var ex);
        Assert.Null(ex);
        Assert.NotNull(s);
        Assert.Equal("abc", s);
    }

    [Fact]
    public void TaskResultBox()
    {
        // TaskResultBox currently uses a stating field for values before activations are
        // signalled; High Integrity Mode *demands* this behaviour, so: validate that it
        // works correctly
        var msg = Message.Create(-1, CommandFlags.None, RedisCommand.PING, CancellationToken.None);
        var box = TaskResultBox<string>.Create(msg.CancellationToken, out var tcs, null);
        Assert.True(box.IsAsync);

        msg.SetSource(ResultProcessor.DemandOK, box);
        Assert.True(msg.TrySetResult("abc"));

        // check that continuation was not already signalled
        Thread.Sleep(100);
        Assert.False(tcs.Task.IsCompleted);

        msg.SetSource(ResultProcessor.DemandOK, box);
        Assert.True(msg.TrySetResult("abc"));

        // check that TrySetResult did not signal continuation
        Thread.Sleep(100);
        Assert.False(tcs.Task.IsCompleted);

        // check that complete signals continuation
        msg.Complete();
        Thread.Sleep(100);
        Assert.True(tcs.Task.IsCompleted);

        var s = box.GetResult(out var ex);
        Assert.Null(ex);
        Assert.NotNull(s);
        Assert.Equal("abc", s);

        Assert.Equal("abc", tcs.Task.Result); // we already checked IsCompleted
    }
}
