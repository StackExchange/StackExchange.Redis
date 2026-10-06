using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using RESPite;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A synchronous call that gave up must not be able to wake the NEXT call on the same thread.
/// </summary>
/// <remarks>
/// The pump is reused per thread, and a call that timed out leaves its "done" signal registered on an operation
/// that is still running. When that operation finished, the signal landed on whichever call the thread was making
/// by then, which left the pump and blocked in <c>GetResult</c> on its own operation with no deadline. On a full
/// run that was a synchronous <c>HashFieldExpireNoField</c> parked for minutes, until the hang watchdog killed
/// the test host.
/// </remarks>
public class SyncPumpStaleSignalTests
{
    [Fact]
    public void AStaleSignalDoesNotReleaseTheNextCall()
    {
        var executor = new HeldReplies();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.TimeoutMilliseconds.Returns(300);
        IDatabase db = new RespDatabaseContext(new RespContext().WithExecutor(executor)).AsDatabase(multiplexer);

        using var secondStarted = new ManualResetEventSlim();
        Exception? first = null, second = null;
        var caller = new Thread(() =>
        {
            first = Record(() => db.StringGet("a"));   // times out; its operation stays pending
            secondStarted.Set();
            second = Record(() => db.StringGet("b"));  // never answered: must time out, not hang
        }) { IsBackground = true };
        caller.Start();

        Assert.True(secondStarted.Wait(10_000), "the first call never returned");
        Thread.Sleep(100);                              // the second call is now waiting in the pump
        executor.Answer(0, "$1\r\na\r\n");              // the FIRST call's operation completes: the stale signal

        Assert.True(caller.Join(10_000), "the second call hung after a stale signal");
        Assert.True(first is TimeoutException, $"first: {first}");
        Assert.True(second is TimeoutException, $"second: {second}"); // not released early by the first call's signal
    }

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Holds every reply until the test answers it; does not time anything out itself.</summary>
    private sealed class HeldReplies : RespExecutorBase
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<RespPayload>> _replies = new();
        private int _next = -1;

        public override int Database => 0;

        internal void Answer(int index, string reply)
            => _replies[index].TrySetResult(RespPayload.Create(Encoding.UTF8.GetBytes(reply)));

        public override RespPayload Send(in RespRequest request) => throw new NotSupportedException();

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            var source = new TaskCompletionSource<RespPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            _replies[Interlocked.Increment(ref _next)] = source;
            return new(source.Task);
        }
    }
}
