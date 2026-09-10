using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="SimpleResultBox{T}"/> boxes are pooled per-thread, so a synchronous caller must not stop
/// waiting on one until the completing thread has finished with it. Faulting the box
/// (<c>ResultProcessor.SetException</c>) and pulsing it (<c>Message.Complete</c> -&gt;
/// <c>ActivateContinuations</c>) are separate, unsynchronized steps: a waiter that leaves on the fault
/// alone can recycle the box while the pulse is still inbound, and that pulse then wakes whichever
/// operation next borrows the box - which returns with neither result nor exception, shifting every
/// later reply on that thread by one. Reported against Garnet CI as microsoft/garnet#2110.
/// </summary>
public class ResultBoxCompletionTests(ITestOutputHelper log)
{
    /// <summary>
    /// A fault published by another thread must not be mistaken for completion: that thread still owes
    /// us the pulse, and it is only safe to recycle the box once the pulse has been delivered.
    /// </summary>
    [Fact]
    public void FaultAloneIsNotCompletion()
    {
        var box = SimpleResultBox<int>.Get();
        using var faulted = new ManualResetEventSlim();
        var pulsedAt = 0L;

        var completer = Task.Run(() =>
        {
            box.SetException(new InvalidOperationException("boom"));
            faulted.Set();
            Thread.Sleep(200); // the real gap between ResultProcessor.SetException and Message.Complete
            Volatile.Write(ref pulsedAt, Stopwatch.GetTimestamp());
            box.ActivateContinuations();
        });

        lock (box)
        {
            Assert.True(faulted.Wait(5000));

            // this is the state the old code bailed out on - and doing so is what recycles a box that
            // is still owed a pulse
            Assert.True(box.IsFaulted);
            Assert.False(box.IsCompleted);

            while (!box.IsCompleted)
            {
                Assert.True(Monitor.Wait(box, 5000), "timed out waiting for completion");
            }
        }

        var leftAt = Stopwatch.GetTimestamp();
        completer.Wait(5000);
        log.WriteLine($"left the wait {(leftAt - Volatile.Read(ref pulsedAt)) * 1000.0 / Stopwatch.Frequency:F2}ms after the pulse");
        Assert.True(leftAt >= Volatile.Read(ref pulsedAt), "waiter left before the completing thread pulsed");

        var value = box.GetResult(out var ex, canRecycle: true);
        Assert.Equal(0, value);
        Assert.IsType<InvalidOperationException>(ex);
    }

    /// <summary>Recycling must hand back a box with no completion or fault left on it.</summary>
    [Fact]
    public void RecyclingResetsCompletionState()
    {
        var box = SimpleResultBox<int>.Get();
        box.SetException(new InvalidOperationException("boom"));
        box.ActivateContinuations();
        Assert.True(box.IsCompleted);
        Assert.True(box.IsFaulted);

        box.GetResult(out _, canRecycle: true);

        var reused = SimpleResultBox<int>.Get();
        Assert.Same(box, reused); // same thread, so we get the pooled instance back
        Assert.False(reused.IsCompleted);
        Assert.False(reused.IsFaulted);
    }

    /// <summary>
    /// A pulse the waiter did not cause must not shorten the wait - the sync path treats any wake as
    /// "the reply is here", so a stale pulse would otherwise be reported as a successful (empty) result.
    /// </summary>
    [Fact]
    public void StrayPulseDoesNotEndTheWait()
    {
        var box = SimpleResultBox<int>.Get();
        using var strayed = new ManualResetEventSlim();

        _ = Task.Run(() =>
        {
            Thread.Sleep(100);
            lock (box) { Monitor.PulseAll(box); } // a pulse with no completion behind it
            strayed.Set();
            Thread.Sleep(100);
            box.SetResult(42);
            box.ActivateContinuations();
        });

        lock (box)
        {
            while (!box.IsCompleted)
            {
                Assert.True(Monitor.Wait(box, 5000), "timed out waiting for completion");
            }
        }

        Assert.True(strayed.IsSet);
        var value = box.GetResult(out var ex, canRecycle: true);
        Assert.Null(ex);
        Assert.Equal(42, value);
    }
}
