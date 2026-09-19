using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Stale-while-revalidate: an ageing entry is served <b>and</b> refreshed, so it never goes from good to
/// gone in one step - which is the moment every concurrent reader of a hot key misses at once.
/// </summary>
/// <remarks>
/// See design notes 6.15. The refresh needs no factory: the cache key <i>is</i> the request.
/// </remarks>
public class RespStaleWhileRevalidateTests
{
    private sealed class CountingExecutor(params string[] replies) : RespExecutorBase
    {
        private int _next;
        private int _sends;

        internal int Sends => Volatile.Read(ref _sends);

        public override int Database => 0;

        public override RespPayload Send(in RespRequest request)
        {
            Interlocked.Increment(ref _sends);
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;

    private static ValueTask<RedisValue> Get(RespDatabaseContext context)
        => context.SendAsync<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

    /// <summary>The refresh runs on the thread pool, so wait for it rather than guessing at a delay.</summary>
    private static async Task<bool> WaitFor(Func<bool> condition, int millis = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < millis)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return condition();
    }

    private static RespDatabaseContext Context(CountingExecutor executor, RespClientCache cache)
        => new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));

    [Fact]
    public async Task AnAgeingEntryIsServedAndRefreshed()
    {
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy
        {
            RefreshAfter = TimeSpan.FromMilliseconds(60),
            TimeToLive = TimeSpan.FromMinutes(5),
        } });
        var executor = new CountingExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));
        Assert.Equal(1, executor.Sends);

        await Task.Delay(150);   // now past the soft threshold, nowhere near the hard one

        // served from cache - the caller does NOT wait for the refresh, which is the whole point
        Assert.Equal("a", await Get(context));

        // ...and a refresh was started behind it
        Assert.True(await WaitFor(() => executor.Sends == 2), "no background refresh was started");
        Assert.Equal(1, cache.Refreshes);

        // and the refreshed value is what the next read sees, without anybody having waited for it
        Assert.True(await WaitFor(() => cache.Stored == 2), "the refresh never landed in the cache");
        Assert.Equal("b", await Get(context));
        Assert.Equal(2, executor.Sends);   // still just the original and the one refresh
    }

    [Fact]
    public async Task OnlyOneReaderRefreshes()
    {
        // without the claim, every reader past the threshold starts a refresh - the background work would
        // be the stampede it exists to prevent
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy
        {
            RefreshAfter = TimeSpan.FromMilliseconds(50),
            TimeToLive = TimeSpan.FromMinutes(5),
        } });
        var executor = new CountingExecutor("$1\r\na\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));
        await Task.Delay(120);

        for (var i = 0; i < 10; i++) Assert.Equal("a", await Get(context));

        await Task.Delay(200);
        Assert.Equal(1, cache.Refreshes);           // ten readers, one refresh
        Assert.True(executor.Sends <= 2, $"expected 1 original + at most 1 refresh, saw {executor.Sends}");
    }

    [Fact]
    public async Task AFreshEntryIsNotRefreshed()
    {
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy
        {
            RefreshAfter = TimeSpan.FromMinutes(1),
            TimeToLive = TimeSpan.FromMinutes(5),
        } });
        var executor = new CountingExecutor("$1\r\na\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));
        Assert.Equal("a", await Get(context));

        await Task.Delay(100);
        Assert.Equal(1, executor.Sends);
        Assert.Equal(0, cache.Refreshes);
    }

    [Fact]
    public async Task RefreshIsOffByDefault()
    {
        // serving a value already known to be old is a decision, not an inherited default
        Assert.Equal(TimeSpan.Zero, CachePolicy.Default.RefreshAfter);

        using var cache = new RespClientCache();
        var executor = new CountingExecutor("$1\r\na\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));
        await Task.Delay(100);
        Assert.Equal("a", await Get(context));

        await Task.Delay(100);
        Assert.Equal(1, executor.Sends);
        Assert.Equal(0, cache.Refreshes);
    }

    [Fact]
    public async Task ReplacingAnEntryReleasesTheSupersededReply()
    {
        // the refresh swaps the value in place, and the reply it displaced holds a pooled buffer. Leaking
        // that reference would be invisible - the cache keeps working, it just never gives the buffer back.
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy
        {
            RefreshAfter = TimeSpan.FromMilliseconds(50),
            TimeToLive = TimeSpan.FromMinutes(5),
        } });
        var executor = new CountingExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));

        // hold our own reference to the first reply so we can watch what the cache does with its one
        Assert.True(cache.TryGet(RenderKey(context.Raw), 0, long.MaxValue, out var firstReply));
        Assert.Equal(2, firstReply!.RefCount);   // the cache holds one, TryGet retained another for us

        await Task.Delay(120);
        Assert.Equal("a", await Get(context));                       // stale serve, refresh started
        Assert.True(await WaitFor(() => cache.Stored == 2), "the refresh never landed");

        // the cache has let go of the old reply; only our own reference keeps it alive
        Assert.True(await WaitFor(() => firstReply.RefCount == 1), $"superseded reply still at {firstReply.RefCount}");

        firstReply.Release();
        Assert.Equal("b", await Get(context));
        Assert.Equal(1, cache.Count);            // replaced, not duplicated
    }

    // ---- invalidation-triggered, the opt-in half -----------------------------------------------------

    [Fact]
    public async Task AnInvalidatedEntryIsServedBrieflyAndRefreshed()
    {
        // the stampede that matters most: an invalidation lands for EVERY reader of a hot key at the same
        // instant, so time-based smoothing cannot help - the trigger was not time
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy
        {
            InvalidationGracePeriod = TimeSpan.FromSeconds(5),
            TimeToLive = TimeSpan.FromMinutes(5),
        } });
        var executor = new CountingExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));
        Assert.Equal(1, executor.Sends);

        cache.OnInvalidate(Encoding.UTF8.GetBytes("k"));   // somebody else wrote it

        // still answered - knowingly out of date, and counted as such
        Assert.Equal("a", await Get(context));
        Assert.Equal(1, cache.ServedStale);

        // ...with a refresh started behind it, so the next reader gets the new value
        Assert.True(await WaitFor(() => cache.Stored == 2), "no refresh followed the invalidation");
        Assert.Equal("b", await Get(context));
    }

    [Fact]
    public async Task OurOwnWriteIsNeverServedThrough()
    {
        // read-your-own-writes. "No observer can prove the order" excuses serving through somebody else's
        // write; it says nothing about ours, and returning the value the caller just replaced is reported
        // as corruption rather than as staleness.
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy
        {
            InvalidationGracePeriod = TimeSpan.FromSeconds(5),
            TimeToLive = TimeSpan.FromMinutes(5),
        } });
        var executor = new CountingExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));

        cache.OnLocalWrite(Encoding.UTF8.GetBytes("k"));   // WE wrote it

        Assert.Equal("b", await Get(context));             // a real miss, not a stale serve
        Assert.Equal(0, cache.ServedStale);
        Assert.Equal(2, executor.Sends);
    }

    [Fact]
    public async Task ALocalWriteStillCountsAfterAServerInvalidation()
    {
        // the two can arrive in either order - our own write echoes back from the server as well - and the
        // fact that WE wrote it must survive that
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy
        {
            InvalidationGracePeriod = TimeSpan.FromSeconds(5),
            TimeToLive = TimeSpan.FromMinutes(5),
        } });
        var executor = new CountingExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));

        cache.OnLocalWrite(Encoding.UTF8.GetBytes("k"));
        cache.OnInvalidate(Encoding.UTF8.GetBytes("k"));   // the echo, arriving afterwards

        Assert.Equal("b", await Get(context));
        Assert.Equal(0, cache.ServedStale);
    }

    [Fact]
    public async Task ServingThroughInvalidationIsOffByDefault()
    {
        Assert.Equal(TimeSpan.Zero, CachePolicy.Default.InvalidationGracePeriod);

        using var cache = new RespClientCache();
        var executor = new CountingExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));
        cache.OnInvalidate(Encoding.UTF8.GetBytes("k"));

        Assert.Equal("b", await Get(context));   // straight to the server
        Assert.Equal(0, cache.ServedStale);
    }

    [Fact]
    public async Task TheWindowIsAlsoTheCap()
    {
        // on a hot-written key every refresh is invalidated before it can be stored, so without an absolute
        // bound this would serve stale for ever. Measured from FIRST NOTICE, so it cannot.
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy
        {
            InvalidationGracePeriod = TimeSpan.FromMilliseconds(80),
            TimeToLive = TimeSpan.FromMinutes(5),
        } });
        // The refresh must NOT be allowed to succeed, or it heals the entry and the test cannot tell the cap
        // from the cure. An error reply is refused by TryComplete, so the entry stays invalid - which is
        // precisely the hot-written-key situation the cap is for: every refresh is lost, and without a bound
        // the entry would be served stale for ever.
        var executor = new CountingExecutor("$1\r\na\r\n", "-ERR not today\r\n", "$1\r\nc\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));
        cache.OnInvalidate(Encoding.UTF8.GetBytes("k"));

        Assert.Equal("a", await Get(context));    // inside the window: served stale, notice recorded
        Assert.Equal(1, cache.ServedStale);

        Assert.True(await WaitFor(() => executor.Sends == 2), "the refresh never ran");
        Assert.True(await WaitFor(() => cache.RefusedError == 1), "the refresh was not refused");
        Assert.Equal(1, cache.Count);             // still the original, still invalid

        await Task.Delay(200);                    // past the window

        Assert.Equal("c", await Get(context));    // the window closed; a real fetch
        Assert.Equal(1, cache.ServedStale);       // and NOT another stale serve
    }

    [Fact]
    public async Task AKeyNobodyIsReadingJustExpires()
    {
        // The grace period runs from the INVALIDATION, not from whoever next happens to look. The case
        // worth protecting is a key under constant access, where the herd forms the instant it is
        // invalidated. A key nobody is reading should simply expire - starting the clock at first notice
        // would instead resurrect it for whoever wandered past an hour later, which is the opposite of the
        // intent.
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy
        {
            InvalidationGracePeriod = TimeSpan.FromMilliseconds(80),
            TimeToLive = TimeSpan.FromMinutes(5),
        } });
        var executor = new CountingExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));
        cache.OnInvalidate(Encoding.UTF8.GetBytes("k"));

        // nobody reads it during the grace period
        await Task.Delay(200);

        Assert.Equal("b", await Get(context));   // a real fetch, not a stale serve
        Assert.Equal(0, cache.ServedStale);
        Assert.Equal(0, cache.Refreshes);        // and no background work was started for it either
    }

    [Fact]
    public async Task TheGraceIsNotRestartedByLaterReads()
    {
        // it is a grace period, not a sliding window: constant access bridges the burst, it does not keep
        // the old value alive indefinitely
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy
        {
            InvalidationGracePeriod = TimeSpan.FromMilliseconds(120),
            TimeToLive = TimeSpan.FromMinutes(5),
        } });
        var executor = new CountingExecutor("$1\r\na\r\n", "-ERR not today\r\n", "$1\r\nc\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));
        cache.OnInvalidate(Encoding.UTF8.GetBytes("k"));

        // read repeatedly across the window; the refresh keeps failing, so only the cap can stop this
        var served = 0;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 300)
        {
            if ((string?)await Get(context) == "a") served++;
            await Task.Delay(15);
        }

        Assert.True(served > 0, "nothing was served during the grace period");
        Assert.True(
            watch.ElapsedMilliseconds > 250 && (string?)await Get(context) != "a",
            "still serving the old value long after the grace period");
    }

    /// <summary>The same rendered key the surface would produce, for poking the cache directly.</summary>
    private static RespRequest RenderKey(RespContext context)
    {
        var handler = context.Compose($"{RedisCommand.GET}{(RedisKey)"k"}");
        var frame = handler.Complete();
        return frame.Detach(Readable);
    }

    [Fact]
    public async Task AThresholdBeyondTheLifetimeNeverFires()
    {
        // it could never be crossed: the entry expires first. Treated as "off" rather than as a puzzle.
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy
        {
            RefreshAfter = TimeSpan.FromMinutes(10),
            TimeToLive = TimeSpan.FromMilliseconds(80),
        } });
        var executor = new CountingExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));
        await Task.Delay(200);

        Assert.Equal("b", await Get(context));   // a plain expiry, not a stale serve
        Assert.Equal(0, cache.Refreshes);
        Assert.True(cache.Expired > 0);
    }
}
