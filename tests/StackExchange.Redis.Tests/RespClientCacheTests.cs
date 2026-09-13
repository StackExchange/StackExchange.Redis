using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The two-table client-side cache: (frame, db) => (payload, generations), and key => generation. No server
/// involved - invalidation is poked in from outside, exactly as a push handler would.
/// </summary>
public class RespClientCacheTests
{
    private static readonly RespContext Ctx = new();

    private static RespFrame Get(string key) => Ctx.Execute($"{RedisCommand.GET}{(RedisKey)key}");

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string Text(ReadOnlySpan<byte> value) =>
        Encoding.UTF8.GetString(value.ToArray()).Replace("\r\n", "|");

    /// <summary>Render, fill, and cache - the normal miss-then-populate path.</summary>
    private static void Fill(RespClientCache cache, string key, string response, int database = 0)
    {
        var frame = Get(key);
        Assert.True(cache.TryBeginFill(ref frame, database, out var fill));
        Assert.True(cache.TryComplete(fill, Utf8(response)));
    }

    private static bool TryRead(RespClientCache cache, string key, out string text, int database = 0)
    {
        using var frame = Get(key);
        if (cache.TryGet(frame.AsLookupKey(), database, out var payload))
        {
            try
            {
                text = Text(payload.Span);
                return true;
            }
            finally
            {
                payload.Release();
            }
        }

        text = "";
        return false;
    }

    [Fact]
    public void FillThenHit()
    {
        using var cache = new RespClientCache();
        Fill(cache, "abc", "$5\r\nhello\r\n");

        Assert.True(TryRead(cache, "abc", out var text));
        Assert.Equal("$5|hello|", text);
        Assert.False(TryRead(cache, "other", out _));
    }

    [Fact]
    public void InvalidateEvictsLogically()
    {
        using var cache = new RespClientCache();
        Fill(cache, "abc", "$5\r\nhello\r\n");
        Assert.True(TryRead(cache, "abc", out _));

        Assert.True(cache.OnInvalidate(Utf8("abc")));
        Assert.False(TryRead(cache, "abc", out _));

        // the entry is still resident until a sweep - invalidation deliberately does no more than stamp
        Assert.Equal(1, cache.Count);
        Assert.Equal(1, cache.Sweep());
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void InvalidatingAnUncachedKeyIsCheapAndReportsFalse()
    {
        using var cache = new RespClientCache();
        Fill(cache, "abc", "$5\r\nhello\r\n");

        Assert.False(cache.OnInvalidate(Utf8("not-cached")));
        Assert.True(TryRead(cache, "abc", out _)); // untouched
    }

    [Fact]
    public void InvalidationIsAllocationFree()
    {
        using var cache = new RespClientCache();
        Fill(cache, "abc", "$5\r\nhello\r\n");

        var hit = Utf8("abc");
        var miss = Utf8("some:other:key:that:is:not:here");
        for (var i = 0; i < 500; i++)
        {
            cache.OnInvalidate(hit);
            cache.OnInvalidate(miss);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            cache.OnInvalidate(miss); // the broadcasting flood: keys we do not have
        }

        // BCAST hands us every key touched on the server; this path must not allocate at all
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ReviveAfterInvalidationDoesNotResurrectTheOldEntry()
    {
        using var cache = new RespClientCache();
        Fill(cache, "abc", "$3\r\nold\r\n");
        cache.OnInvalidate(Utf8("abc"));
        cache.Sweep();

        Fill(cache, "abc", "$3\r\nnew\r\n");
        Assert.True(TryRead(cache, "abc", out var text));

        // the refilled entry must be the NEW one: this is why generations are global tickets rather than
        // per-key counters, which would restart and collide with what the old entry recorded
        Assert.Equal("$3|new|", text);
    }

    [Fact]
    public void InvalidationCrossesDatabases()
    {
        using var cache = new RespClientCache();
        Fill(cache, "abc", "$2\r\nd0\r\n", database: 0);
        Fill(cache, "abc", "$2\r\nd7\r\n", database: 7);

        Assert.True(TryRead(cache, "abc", out _, database: 0));
        Assert.True(TryRead(cache, "abc", out _, database: 7));

        cache.OnInvalidate(Utf8("abc"));

        // Redis tracking uses one keyspace regardless of database, so both must go
        Assert.False(TryRead(cache, "abc", out _, database: 0));
        Assert.False(TryRead(cache, "abc", out _, database: 7));
    }

    [Fact]
    public void DifferentDatabasesAreSeparateEntries()
    {
        using var cache = new RespClientCache();
        Fill(cache, "abc", "$2\r\nd0\r\n", database: 0);

        Assert.True(TryRead(cache, "abc", out var zero, database: 0));
        Assert.Equal("$2|d0|", zero);
        Assert.False(TryRead(cache, "abc", out _, database: 7)); // not the same entry
    }

    [Fact]
    public void FlushDropsEverything()
    {
        using var cache = new RespClientCache();
        Fill(cache, "a", "$1\r\na\r\n");
        Fill(cache, "b", "$1\r\nb\r\n");

        cache.OnFlush(); // null invalidation, or a lost connection

        Assert.False(TryRead(cache, "a", out _));
        Assert.False(TryRead(cache, "b", out _));
        Assert.Equal(2, cache.Sweep());
    }

    /// <summary>
    /// The race that matters: an invalidation lands while the command is in flight. Caching the reply would
    /// leave PERMANENTLY stale data, because the server dropped the key from its invalidation table when it
    /// fired and will not tell us again.
    /// </summary>
    [Fact]
    public void InvalidationDuringFlightRefusesTheFill()
    {
        using var cache = new RespClientCache();

        var frame = Get("abc");
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill)); // generations captured at SEND time

        cache.OnInvalidate(Utf8("abc")); // ... someone writes the key while we wait for the reply ...

        Assert.False(cache.TryComplete(fill, Utf8("$5\r\nstale\r\n")));
        Assert.Equal(0, cache.Count);
        Assert.False(TryRead(cache, "abc", out _));
    }

    [Fact]
    public void InvalidationBeforeTheFillStartsDoesNotBlockIt()
    {
        using var cache = new RespClientCache();
        Fill(cache, "abc", "$3\r\nold\r\n");
        cache.OnInvalidate(Utf8("abc"));
        cache.Sweep();

        // the invalidation preceded this request, so its reply reflects the write and is cacheable
        var frame = Get("abc");
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.True(cache.TryComplete(fill, Utf8("$5\r\nfresh\r\n")));
        Assert.True(TryRead(cache, "abc", out var text));
        Assert.Equal("$5|fresh|", text);
    }

    [Fact]
    public void FramesWhoseKeysCannotBeEnumeratedAreNotCached()
    {
        using var cache = new RespClientCache();

        // three keys exceeds the two inline marks, and the overflow path records nothing usable - so the
        // keys cannot be named, so the entry could never be invalidated. Refusing is the safe answer.
        var frame = Ctx.Execute($"{RedisCommand.DEL}{(RedisKey)"a"}{(RedisKey)"b"}{(RedisKey)"c"}");
        Assert.True(frame.KeysNeedScan);
        Assert.False(cache.TryBeginFill(ref frame, 0, out _));
        frame.Dispose();

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void MultiKeyEntryIsInvalidatedByAnyOfItsKeys()
    {
        using var cache = new RespClientCache();

        var frame = Ctx.Execute($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}");
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.True(cache.TryComplete(fill, Utf8("*2\r\n$1\r\n1\r\n$1\r\n2\r\n")));

        using (var probe = Ctx.Execute($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}"))
        {
            Assert.True(cache.TryGet(probe.AsLookupKey(), 0, out var payload));
            payload.Release();
        }

        cache.OnInvalidate(Utf8("b")); // the SECOND key

        using (var probe = Ctx.Execute($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}"))
        {
            Assert.False(cache.TryGet(probe.AsLookupKey(), 0, out _));
        }
    }

    [Fact]
    public void KeyTableGrowsWithoutLosingTrackedKeys()
    {
        using var cache = new RespClientCache(keyCapacity: 4);
        for (var i = 0; i < 400; i++) Fill(cache, "key:" + i, "$1\r\nx\r\n");

        Assert.Equal(400, cache.Count);
        Assert.Equal(400, cache.TrackedKeyCount);

        // growth carries nodes over BY REFERENCE; if it had rebuilt them, every entry's dependency would
        // point at an orphan and invalidation would silently stop working
        for (var i = 0; i < 400; i++) Assert.True(cache.OnInvalidate(Utf8("key:" + i)));
        for (var i = 0; i < 400; i++) Assert.False(TryRead(cache, "key:" + i, out _));
    }

    [Fact]
    public async Task ConcurrentInvalidationAndReadsNeverServeStale()
    {
        for (var round = 0; round < 100; round++)
        {
            using var cache = new RespClientCache();
            Fill(cache, "abc", "$5\r\nhello\r\n");

            var start = new ManualResetEventSlim(false);
            var readers = new Task[6];
            for (var i = 0; i < readers.Length; i++)
            {
                readers[i] = Task.Run(() =>
                {
                    start.Wait();
                    // whatever we get must be intact; after the invalidation it must simply be a miss
                    if (TryRead(cache, "abc", out var text)) Assert.Equal("$5|hello|", text);
                });
            }

            var invalidator = Task.Run(() => { start.Wait(); cache.OnInvalidate(Utf8("abc")); });

            start.Set();
            await Task.WhenAll(readers);
            await invalidator;

            Assert.False(TryRead(cache, "abc", out _)); // settled state: gone
        }
    }
}
