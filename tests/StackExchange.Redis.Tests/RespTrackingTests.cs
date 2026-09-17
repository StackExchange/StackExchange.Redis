using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
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
[Collection(NonParallelCollection.Name)] // see RespCacheInvalidationTests: flush pushes cross connections
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

    /// <summary>
    /// Run a write of our own and wait until its echo has actually arrived and traffic has gone quiet.
    /// </summary>
    /// <remarks>
    /// Waiting for quiet is not enough by itself, and this is the subtle part: under load the echo may not
    /// have arrived <i>at all</i> yet, so two consecutive samples read the same number and "settled" is
    /// concluded before the push lands - which then turns up later and spoils whatever baseline the test
    /// took. So: wait <b>for</b> the echo we know is coming, and only then wait for quiet.
    /// </remarks>
    private static async Task WriteAndSettleAsync(TrackingExecutor executor, params string[] command)
    {
        var before = executor.Invalidations;
        await executor.CommandAsync(command);

        Assert.True(
            await WaitFor(() => executor.Invalidations > before),
            "our own write did not echo back - is NOLOOP on?");

        await SettleAsync(executor);
    }

    private static bool Contains(TrackingExecutor executor, string key)
    {
        lock (executor.InvalidatedKeys) return executor.InvalidatedKeys.Contains(key);
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
        await WriteAndSettleAsync(executor, "SET", key, "first");

        Assert.Equal("first", await context.Strings.GetAsync(key));
        Assert.Equal(1, cache.Count);

        // served from cache: the server never sees the second read
        Assert.Equal("first", await context.Strings.GetAsync(key));
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
        Assert.Equal("second", await context.Strings.GetAsync(key));
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
        await WriteAndSettleAsync(executor, "SET", key, "value");

        Assert.Equal("value", await context.Strings.GetAsync(key));
        Assert.Equal(1, cache.Count);

        await using var other = Create();
        await other.GetDatabase().StringSetAsync(key, "changed");

        // The real assertion, made directly: the server named the same bytes we wrote. Comparing the key
        // it sent beats inspecting cache state, which is not reliable on a shared server - any concurrent
        // FLUSHDB sends an unfilterable flush push that can invalidate our entry before it is even stored.
        Assert.True(
            await WaitFor(() => Contains(executor, key)),
            "the server never named this key - its bytes did not match ours");

        Assert.Equal("changed", await context.Strings.GetAsync(key));
    }

    [Fact]
    public async Task OneWriteTouchingSeveralKeysInvalidatesAllOfThem()
    {
        var (executor, cache, context) = await Tracked(Me());
        using var _ = executor;
        using var __ = cache;

        var prefix = Me();
        string A = prefix + ":a", B = prefix + ":b";
        await WriteAndSettleAsync(executor, "MSET", A, "1", B, "2");
        var seenBefore = executor.KeysInvalidated;

        Assert.Equal("1", await context.Strings.GetAsync(A));
        Assert.Equal("2", await context.Strings.GetAsync(B));
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
        await WriteAndSettleAsync(executor, "SET", key, "value");
        Assert.Equal("value", await context.Strings.GetAsync(key));

        var channel = Me() + ":channel";
        await executor.CommandAsync("SUBSCRIBE", channel);

        await using var other = Create();
        await other.GetSubscriber().PublishAsync(RedisChannel.Literal(channel), "hello");

        // Assert the POSITIVE - that the delivery was classified as a delivery. Asserting the negative
        // ("no invalidation arrived") looks equivalent and is not: it is a claim about a global counter,
        // so any unrelated traffic on this connection fails it, and it flaked under suite load.
        Assert.True(await WaitFor(() => executor.Deliveries > 0), "the pub/sub delivery never arrived");

        // Deliberately NOT asserting that our cached entry survived. That looks like the natural companion
        // assertion and it is not testable on a shared server: a FLUSHDB by ANY concurrent test, on ANY
        // database, sends every tracking client an unfilterable `invalidate null` - PREFIX cannot scope a
        // flush, because a flush names no keys. Verified against the server. So the entry legitimately
        // disappears at random here, and asserting otherwise tests the suite's scheduling, not the code.
        Assert.Equal("value", await context.Strings.GetAsync(key));
    }

    [Fact]
    public async Task AFlushDropsEverything()
    {
        var (executor, cache, context) = await Tracked(Me());
        using var _ = executor;
        using var __ = cache;

        var key = Me();
        await WriteAndSettleAsync(executor, "SET", key, "value");
        Assert.Equal("value", await context.Strings.GetAsync(key));
        Assert.Equal(1, cache.Count);

        // The only destructive test here. FLUSHDB on the shared primary would wipe the database out from
        // under every concurrently-running test - which is exactly what it did the first time - so it goes
        // to a database this suite hands out for the purpose. Hard-coding an index is not good enough
        // either: GetDedicatedDB is a monotonic counter, so a "surely nobody uses 9" eventually collides
        // with whoever gets 9. Tracking is database-agnostic, so the push arrives regardless.
        await using var other = Create(allowAdmin: true);
        var scratch = TestConfig.GetDedicatedDB(other);
        await other.GetDatabase(scratch).StringSetAsync(key, "scratch");
        await other.GetServer(TestConfig.Current.PrimaryServerAndPort).FlushDatabaseAsync(scratch);

        Assert.True(await WaitFor(() => executor.Flushes > 0), "the flush arrived as a key list rather than a null");
        Assert.Equal(1, cache.Sweep());
    }
}
