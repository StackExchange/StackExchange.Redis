using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interpolated;
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
    private sealed class CountingExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;
        private int _sends;

        internal int Sends => Volatile.Read(ref _sends);

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Interlocked.Increment(ref _sends);
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;

    private static ValueTask<RedisValue> Get(RespContext context)
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

    private static RespContext Context(CountingExecutor executor, RespClientCache cache)
        => new RespContext().WithExecutor(executor).WithCache(cache);

    [Fact]
    public async Task AnAgeingEntryIsServedAndRefreshed()
    {
        using var cache = new RespClientCache(new CachePolicy
        {
            RefreshAfter = TimeSpan.FromMilliseconds(60),
            TimeToLive = TimeSpan.FromMinutes(5),
        });
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
        using var cache = new RespClientCache(new CachePolicy
        {
            RefreshAfter = TimeSpan.FromMilliseconds(50),
            TimeToLive = TimeSpan.FromMinutes(5),
        });
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
        using var cache = new RespClientCache(new CachePolicy
        {
            RefreshAfter = TimeSpan.FromMinutes(1),
            TimeToLive = TimeSpan.FromMinutes(5),
        });
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
        using var cache = new RespClientCache(new CachePolicy
        {
            RefreshAfter = TimeSpan.FromMilliseconds(50),
            TimeToLive = TimeSpan.FromMinutes(5),
        });
        var executor = new CountingExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));

        // hold our own reference to the first reply so we can watch what the cache does with its one
        Assert.True(cache.TryGet(RenderKey(context), 0, long.MaxValue, out var firstReply));
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
        using var cache = new RespClientCache(new CachePolicy
        {
            RefreshAfter = TimeSpan.FromMinutes(10),
            TimeToLive = TimeSpan.FromMilliseconds(80),
        });
        var executor = new CountingExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = Context(executor, cache);

        Assert.Equal("a", await Get(context));
        await Task.Delay(200);

        Assert.Equal("b", await Get(context));   // a plain expiry, not a stale serve
        Assert.Equal(0, cache.Refreshes);
        Assert.True(cache.Expired > 0);
    }
}
