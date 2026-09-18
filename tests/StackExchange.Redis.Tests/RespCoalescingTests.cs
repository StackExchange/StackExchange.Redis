using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Single-flight: concurrent misses on the same request wait for one round trip instead of each sending
/// their own. See design notes 6.15.
/// </summary>
public class RespCoalescingTests
{
    /// <summary>An executor whose replies are held until the test lets them go.</summary>
    private sealed class GatedExecutor(string reply)
    {
        private readonly TaskCompletionSource<bool> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Sends;

        internal Executor Interface => new(this, reply);

        internal void Release() => _gate.TrySetResult(true);

        internal void Fail() => _gate.TrySetException(new InvalidOperationException("boom"));

        internal Task Gate => _gate.Task;

        internal sealed class Executor(GatedExecutor owner, string reply) : IRespExecutor
        {
            public int Database => 0;

            public RespPayload Send(in RespRequest request) => throw new NotSupportedException("async only");

            public async ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref owner.Sends);
                await owner.Gate.ConfigureAwait(false);
                return RespPayload.Create(Encoding.UTF8.GetBytes(reply));
            }
        }
    }

    private const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;

    private static ValueTask<RedisValue> Get(RespDatabaseContext context, RedisKey key)
        => context.SendAsync<RedisValue>($"{RedisCommand.GET}{key}", Readable);

    [Fact]
    public async Task ConcurrentMissesShareOneRoundTrip()
    {
        using var cache = new RespClientCache();
        var gated = new GatedExecutor("$5\r\nhello\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(gated.Interface).WithCache(cache));

        // the probe and the registration happen synchronously, before the first await - so by the time
        // this returns, the second caller has something to attach to
        var first = Get(context, "k");
        Assert.Equal(1, cache.InFlightCount);

        var second = Get(context, "k");
        var third = Get(context, "k");

        gated.Release();

        Assert.Equal("hello", (string?)await first);
        Assert.Equal("hello", (string?)await second);
        Assert.Equal("hello", (string?)await third);

        Assert.Equal(1, gated.Sends);       // this is the whole point
        Assert.Equal(2, cache.Coalesced);
        Assert.Equal(0, cache.RedundantFills);
        Assert.Equal(0, cache.InFlightCount);
    }

    [Fact]
    public async Task AFailedLeaderDoesNotStrandItsWaiters()
    {
        // a fill that throws must still release its registration, or everyone attached to it waits on a
        // reply that is never coming - and the registration would linger, catching later callers too
        using var cache = new RespClientCache();
        var gated = new GatedExecutor("$5\r\nhello\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(gated.Interface).WithCache(cache));

        var first = Get(context, "k");
        var second = Get(context, "k");

        gated.Fail();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await first);

        // the waiter wakes, finds nothing cached, and fetches for itself - which also throws, because this
        // executor is still failing. What matters is that it COMPLETED rather than hanging.
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
        Assert.Equal(0, cache.InFlightCount);
    }

    [Fact]
    public async Task AWriteWhileInFlightPreventsAttaching()
    {
        // read-your-own-writes. The leader's reply predates the write, so a caller arriving after the write
        // must NOT be given it - it fetches for itself instead. Same invariant that guards the store.
        using var cache = new RespClientCache();
        var gated = new GatedExecutor("$5\r\nhello\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(gated.Interface).WithCache(cache));

        var first = Get(context, "k");
        Assert.Equal(1, cache.InFlightCount);

        cache.OnInvalidate(Encoding.UTF8.GetBytes("k")); // something wrote it

        var second = Get(context, "k");
        gated.Release();

        Assert.Equal("hello", (string?)await first);
        Assert.Equal("hello", (string?)await second);

        Assert.Equal(2, gated.Sends);   // the second did NOT attach
        Assert.Equal(0, cache.Coalesced);
    }
}
