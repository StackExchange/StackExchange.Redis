using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What a lease of windows costs against a lease of values, which is the whole argument for
/// <see cref="RespValue"/>.
/// </summary>
public class RespValueAllocationTests(ITestOutputHelper log)
{
    private const int Elements = 1000;

    /// <summary>An MGET reply of <see cref="Elements"/> values, each too long to pack inline.</summary>
    private static byte[] Reply()
    {
        var payload = new string('x', 32); // > RedisValue.MaxInlineBytes, and not a canonical number
        var sb = new StringBuilder().Append('*').Append(Elements).Append("\r\n");
        for (var i = 0; i < Elements; i++)
        {
            sb.Append('$').Append(payload.Length).Append("\r\n").Append(payload).Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static long Measure(Action action)
    {
        action(); // let anything one-off settle before counting

        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void WindowsCostFarLessThanValues()
    {
        var reply = Reply();

        var asValues = Measure(() =>
        {
            var reader = new RespReader(reply);
            reader.MoveNext();
            using var lease = RespHandlers.ValueLease.Parse(ref reader);
            Assert.Equal(Elements, lease.Length);
        });

        var asWindows = Measure(() =>
        {
            using var payload = RespPayload.Create(reply);
            using var lease = ((IRespPayloadHandler<ReadOnlyLease<RespValue>>)RespHandlers.ValueWindowHandler.Lease).Parse(payload);
            Assert.Equal(Elements, lease.Length);
        });

        log.WriteLine($"{Elements} values of 32 bytes: RedisValue {asValues:n0} bytes, RespValue {asWindows:n0} bytes");

        // the claim: a RedisValue element with no lifetime has to own its bytes, so a lease of them is one
        // pooled array PLUS an allocation each; a lease of windows is one pooled array and the one buffer
        // they share. Measured at 56,656B against 736B - 77x - so an order of magnitude is a deliberately
        // loose bar, set where it will not go off for a rounding change but will for a regression.
        Assert.True(
            asWindows * 10 < asValues,
            $"expected windows to cost an order of magnitude less; got {asWindows:n0} vs {asValues:n0}");
    }

    [Fact]
    public void TheWindowsStillReadTheRightValues()
    {
        // cheaper is no good if it is wrong: the same reply, read back through both paths
        var reply = Reply();

        using var payload = RespPayload.Create(reply);
        using var windows = ((IRespPayloadHandler<ReadOnlyLease<RespValue>>)RespHandlers.ValueWindowHandler.Lease).Parse(payload);
        var reader = new RespReader(reply);
        reader.MoveNext();
        using var values = RespHandlers.ValueLease.Parse(ref reader);

        Assert.Equal(values.Length, windows.Length);
        for (var i = 0; i < values.Length; i++)
        {
            Assert.Equal(values.Span[i], windows.Span[i].AsRedisValue());
        }
    }

    private sealed class OneReply(byte[] reply) : IRespExecutor
    {
        public int Sends { get; private set; }

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sends++;
            return RespPayload.Create(reply);
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    [Fact]
    public async Task ACacheHitIsReadInPlaceRatherThanCopied()
    {
        // the case the whole design is for: a cached reply is a buffer many callers share, and a window
        // into it costs nothing. The pipeline releases its own reference in a finally as soon as parsing
        // returns, so the values are only readable afterwards because the lease took one of its own.
        var executor = new OneReply(Reply());
        using var cache = new RespClientCache();
        var ctx = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));

        RedisKey[] keys = ["k1", "k2"];

        using (var first = await ctx.Strings.GetAsync(keys, CommandFlags.PreferReplica))
        {
            Assert.Equal(Elements, first.Length);
        }

        using var second = await ctx.Strings.GetAsync(keys, CommandFlags.PreferReplica);

        Assert.Equal(1, executor.Sends); // served from the cache, not the wire
        Assert.Equal(Elements, second.Length);
        Assert.Equal("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", (string?)second.Span[0]);
    }

    [Fact]
    public void TheReplyBufferGoesBackWithTheLease()
    {
        var reply = Reply();
        var payload = RespPayload.Create(reply);

        var lease = ((IRespPayloadHandler<ReadOnlyLease<RespValue>>)RespHandlers.ValueWindowHandler.Lease).Parse(payload);

        // the handler took a reference of its own, so the pipeline releasing its one leaves the values
        // readable - this is what "the lease owns the buffer" has to mean
        payload.Release();
        Assert.Equal("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", (string?)lease.Span[0]);

        // ...and disposing the lease gives back that last reference
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lease.Span.Length);
    }
}
