using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Client-side caching end to end against a real server: read, cache, have somebody else write, and check
/// the invalidation actually arrives and evicts.
/// </summary>
/// <remarks>
/// This is the half of the design that was resting on reasoning rather than evidence. Everything else about
/// the cache is tested against fakes, which can prove the logic but not that a server's invalidation reaches
/// us, nor that the key bytes it names match the ones we recorded when we wrote the command.
/// </remarks>
public class RespTrackingTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    /// <summary>
    /// Wait until invalidation traffic goes quiet, so a test can take a baseline it can trust.
    /// </summary>
    /// <remarks>
    /// <b>Our own writes come back to us.</b> <c>NOLOOP</c> is off, so a setup <c>SET</c> produces an
    /// invalidation push for the key we just wrote, arriving at some point after the reply. A baseline
    /// captured before that lands makes any delta assertion racy - which is exactly how these tests failed
    /// intermittently before this existed. See design notes 6.13 on why NOLOOP is not simply switched on.
    /// </remarks>
    private static async Task SettleAsync(TrackingExecutor executor, int quietMillis = 200)
    {
        var seen = -1;
        while (seen != executor.Invalidations)
        {
            seen = executor.Invalidations;
            await Task.Delay(quietMillis);
        }
    }

    private static async Task<bool> WaitFor(Func<bool> condition, int millis = 2000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < millis)
        {
            if (condition()) return true;
            await Task.Delay(15);
        }

        return condition();
    }

    /// <summary>
    /// A tracking connection scoped to this test's own key prefix.
    /// </summary>
    /// <remarks>
    /// <b>The prefix is not decoration.</b> Under <c>BCAST</c> with no prefix, this connection is told about
    /// every key every other test in the suite touches - which is the chattiness cost of broadcasting, made
    /// concrete. Any assertion counting invalidations is then counting the whole suite's traffic, and these
    /// tests duly failed at random until the prefix went in. It also exercises <c>PREFIX</c> for free.
    /// </remarks>
    private async Task<(TrackingExecutor Executor, RespClientCache Cache, RespContext Context)> Tracked(string prefix)
    {
        var cache = new RespClientCache();
        TrackingExecutor executor;
        try
        {
            executor = await TrackingExecutor.ConnectAsync(
                TestConfig.Current.PrimaryServer, TestConfig.Current.PrimaryPort, cache, prefix);
        }
        catch (Exception ex)
        {
            cache.Dispose();
            Assert.Skip("Unable to connect to server: " + ex.Message);
            throw;
        }

        return (executor, cache, new RespContext().WithExecutor(executor).WithCache(cache));
    }

    [Fact]
    public async Task AThirdPartyWriteInvalidatesWhatWeCached()
    {
        var (executor, cache, context) = await Tracked(Me());
        using var _ = executor;
        using var __ = cache;

        var key = Me();
        await executor.CommandAsync("SET", key, "first");
        await SettleAsync(executor); // let our own write's echo land before we cache anything

        Assert.Equal("first", await context.Strings.Get(key));
        Assert.Equal(1, cache.Count);

        // served from cache: the server never sees the second read
        Assert.Equal("first", await context.Strings.Get(key));
        Assert.Equal(1, cache.Count);

        // somebody else changes it - a different connection entirely
        await using var other = Create();
        await other.GetDatabase().StringSetAsync(key, "second");

        Assert.True(await WaitFor(() => executor.KeysInvalidated > 0), "the invalidation never arrived");

        // invalidation only STAMPS - the entry stays resident until a sweep, so residency is the wrong
        // thing to assert; what matters is that it is no longer readable
        Assert.Equal(1, cache.Sweep());
        Assert.Equal(0, cache.Count);

        // and the next read gets the new value, from the server
        Assert.Equal("second", await context.Strings.Get(key));
    }

    [Fact]
    public async Task TheKeyBytesTheServerSendsAreTheOnesWeRecorded()
    {
        // the assumption underneath all of this: the cache records the key exactly as rendered, and the
        // server invalidates by the name it saw. Non-ASCII is where a mismatch would show up first.
        var (executor, cache, context) = await Tracked(Me());
        using var _ = executor;
        using var __ = cache;

        var key = Me() + ":éü中文";
        await executor.CommandAsync("SET", key, "value");
        await SettleAsync(executor);

        Assert.Equal("value", await context.Strings.Get(key));
        Assert.Equal(1, cache.Count);

        await using var other = Create();
        await other.GetDatabase().StringSetAsync(key, "changed");

        Assert.True(await WaitFor(() => executor.KeysInvalidated > 0), "no invalidation arrived at all");

        // the real assertion: the server's key bytes matched ours, so the stamp landed on OUR entry
        Assert.Equal(1, cache.Sweep());
        Assert.Equal("changed", await context.Strings.Get(key));
    }

    [Fact]
    public async Task OneWriteTouchingSeveralKeysInvalidatesAllOfThem()
    {
        var (executor, cache, context) = await Tracked(Me());
        using var _ = executor;
        using var __ = cache;

        var prefix = Me();
        string A = prefix + ":a", B = prefix + ":b";
        await executor.CommandAsync("MSET", A, "1", B, "2");
        await SettleAsync(executor);
        var seenBefore = executor.KeysInvalidated;

        Assert.Equal("1", await context.Strings.Get(A));
        Assert.Equal("2", await context.Strings.Get(B));
        Assert.Equal(2, cache.Count);

        await using var other = Create();
        await other.GetDatabase().ExecuteAsync("MSET", A, "x", B, "y");

        // one push, several keys - if the handler read only the first, one entry would survive the sweep
        Assert.True(
            await WaitFor(() => executor.KeysInvalidated - seenBefore >= 2),
            "the push did not name both keys");
        Assert.Equal(2, cache.Sweep());
    }

    [Fact]
    public async Task APubSubPushIsNotMistakenForAnInvalidation()
    {
        // Invalidations and pub/sub deliveries are both RESP3 pushes, and telling them apart is the whole
        // of the discrimination the real pipeline has to add. Getting it backwards is not a no-op: a
        // channel name would be read as a key list, or a delivery would be handed back as somebody's reply.
        var (executor, cache, context) = await Tracked(Me());
        using var _ = executor;
        using var __ = cache;

        var key = Me();
        await executor.CommandAsync("SET", key, "value");
        await SettleAsync(executor);
        Assert.Equal("value", await context.Strings.Get(key));

        var channel = Me() + ":channel";
        await executor.CommandAsync("SUBSCRIBE", channel);

        var before = executor.Invalidations;

        await using var other = Create();
        await other.GetSubscriber().PublishAsync(RedisChannel.Literal(channel), "hello");

        // give the delivery time to arrive and be mis-handled, if it is going to be
        await Task.Delay(300);

        Assert.Equal(before, executor.Invalidations);   // the delivery was not counted as an invalidation
        Assert.Equal(1, cache.Count);                   // ...and our entry is untouched
        Assert.Equal("value", await context.Strings.Get(key));
    }

    /// <summary>A database index this suite does not otherwise use, so flushing it disturbs nobody.</summary>
    /// <remarks>
    /// The flush test is the only destructive one here, and <c>FLUSHDB</c> on the shared primary would wipe
    /// the database out from under every test running concurrently - which is exactly what it did the first
    /// time. Tracking is database-agnostic (the server keeps "a single keys namespace, not divided by
    /// database numbers"), so the push arrives regardless of which database was flushed, and confining the
    /// damage costs nothing.
    /// </remarks>
    private const int ScratchDatabase = 9;

    [Fact]
    public async Task AFlushDropsEverything()
    {
        var (executor, cache, context) = await Tracked(Me());
        using var _ = executor;
        using var __ = cache;

        var key = Me();
        await executor.CommandAsync("SET", key, "value");
        await SettleAsync(executor);
        Assert.Equal("value", await context.Strings.Get(key));
        Assert.Equal(1, cache.Count);

        // put something in the scratch database, then flush ONLY that one
        await using var other = Create(allowAdmin: true);
        await other.GetDatabase(ScratchDatabase).StringSetAsync(key, "scratch");
        await other.GetServer(TestConfig.Current.PrimaryServerAndPort).FlushDatabaseAsync(ScratchDatabase);

        Assert.True(await WaitFor(() => executor.Flushes > 0), "the flush arrived as a key list rather than a null");
        Assert.Equal(1, cache.Sweep());
    }
}
