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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ThreeKeyCommandsCacheAndInvalidateOnAnyKey(int which)
    {
        using var cache = new RespClientCache();

        var frame = Ctx.Execute($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}{(RedisKey)"c"}");
        Assert.True(frame.KeysNeedScan);  // beyond the two inline offsets: resolved from the bitmap
        Assert.Equal(3, frame.KeyCount);
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.True(cache.TryComplete(fill, Utf8("*3\r\n$1\r\n1\r\n$1\r\n2\r\n$1\r\n3\r\n")));

        Assert.True(ThreeKeyHit(cache));
        Assert.True(cache.OnInvalidate(Utf8(((char)('a' + which)).ToString())));
        Assert.False(ThreeKeyHit(cache));

        static bool ThreeKeyHit(RespClientCache cache)
        {
            using var probe = Ctx.Execute($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}{(RedisKey)"c"}");
            if (!cache.TryGet(probe.AsLookupKey(), 0, out var payload)) return false;
            payload.Release();
            return true;
        }
    }

    [Fact]
    public void BitmapResolvesTheSameRangesTheOffsetsWould()
    {
        // the two encodings must agree where they overlap, or a frame's keys would depend on how many
        // other keys happened to be present
        using var two = Ctx.Execute($"{RedisCommand.MGET}{(RedisKey)"alpha"}{(RedisKey)"beta"}");
        Assert.False(two.KeysNeedScan);
        Assert.Equal(new[] { "alpha", "beta" }, KeyStrings(two));

        using var three = Ctx.Execute($"{RedisCommand.MGET}{(RedisKey)"alpha"}{(RedisKey)"beta"}{(RedisKey)"gamma"}");
        Assert.True(three.KeysNeedScan);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, KeyStrings(three));
    }

    [Fact]
    public void KeysAreFoundAmongNonKeyArguments()
    {
        // the bitmap indexes ARGUMENTS, so values interleaved with keys must not shift the walk
        using var frame = Ctx.Execute(
            $"{RedisCommand.MSET}{(RedisKey)"k1"}{(RedisValue)"v1"}{(RedisKey)"k2"}{(RedisValue)"v2"}{(RedisKey)"k3"}{(RedisValue)"v3"}");
        Assert.Equal(3, frame.KeyCount);
        Assert.Equal(new[] { "k1", "k2", "k3" }, KeyStrings(frame));
    }

    [Fact]
    public void KeysBeyondTheBitmapAreReportedAsUnavailableNotAsASubset()
    {
        var handler = new RespCommandHandler(0, 70, Ctx, "MGET");
        for (var i = 0; i < 70; i++) handler.AppendFormatted((RedisKey)("k" + i));
        var frame = handler.Complete();

        // argument 63 and beyond have no bit; reporting the first 62 would be worse than reporting none,
        // because a caller tracking keys for invalidation would believe it had them all
        Assert.Equal(-1, frame.KeyCount);
        Span<KeyRange> ranges = stackalloc KeyRange[70];
        Assert.Equal(-1, frame.TryGetKeys(ranges));

        using var cache = new RespClientCache();
        Assert.False(cache.TryBeginFill(ref frame, 0, out _));
        frame.Dispose();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void TooSmallATargetIsRejectedRatherThanTruncated()
    {
        using var frame = Ctx.Execute($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}{(RedisKey)"c"}");
        Span<KeyRange> small = stackalloc KeyRange[2];
        Assert.Equal(-1, frame.TryGetKeys(small));

        Span<KeyRange> exact = stackalloc KeyRange[3];
        Assert.Equal(3, frame.TryGetKeys(exact));
    }

    /// <summary>A command whose reply is a fixed blob; counts how often it was actually issued.</summary>
    private sealed class FakeCommand(string response, Action? onExecute = null) : IRespCommand<string>
    {
        public int Executed { get; private set; }

        public byte[] Execute(ReadOnlySpan<byte> request)
        {
            Executed++;
            onExecute?.Invoke();
            return Utf8(response);
        }

        public string Parse(ReadOnlySpan<byte> response) => Text(response);
    }

    [Fact]
    public void GetOrExecuteRunsOnceThenServesFromCache()
    {
        using var cache = new RespClientCache();
        var command = new FakeCommand("$5\r\nhello\r\n");

        for (var i = 0; i < 3; i++)
        {
            // note: no 'using' on the frame and none on any payload - the helper owns both
            var frame = Get("abc");
            Assert.Equal("$5|hello|", cache.GetOrExecute(ref frame, 0, command));
        }

        Assert.Equal(1, command.Executed);
    }

    [Fact]
    public void GetOrExecuteStillAnswersWhenInvalidatedInFlight()
    {
        using var cache = new RespClientCache();

        // the write lands while our command is in flight - the shape that a hand-written
        // "miss, execute, then add" cannot detect, because by the add there is nothing left to compare
        var command = new FakeCommand("$5\r\nhello\r\n", () => cache.OnInvalidate(Utf8("abc")));

        var frame = Get("abc");
        Assert.Equal("$5|hello|", cache.GetOrExecute(ref frame, 0, command)); // still answered
        Assert.Equal(0, cache.Count);                                          // ... but not cached
    }

    [Fact]
    public void GetOrExecuteAnswersEvenWhenTheFrameCannotBeCached()
    {
        using var cache = new RespClientCache();
        var handler = new RespCommandHandler(0, 70, Ctx, "MGET");
        for (var i = 0; i < 70; i++) handler.AppendFormatted((RedisKey)("k" + i));
        var frame = handler.Complete();

        Assert.Equal("$2|ok|", cache.GetOrExecute(ref frame, 0, new FakeCommand("$2\r\nok\r\n")));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void GetOrExecuteLeavesNoReferenceBehindOnAnyPath()
    {
        using var cache = new RespClientCache();
        var command = new FakeCommand("$5\r\nhello\r\n");

        var fill = Get("abc");
        cache.GetOrExecute(ref fill, 0, command);

        var hit = Get("abc");
        cache.GetOrExecute(ref hit, 0, command);

        // exactly one reference survives - the cache entry's. If the helper leaked the caller's retain the
        // buffer would never return to the pool; if it over-released, the entry would be reading freed bytes
        using var probe = Get("abc"); // borrow; Detach here would own a lease nothing ever released
        Assert.True(cache.TryGet(probe.AsLookupKey(), 0, out var payload));
        Assert.Equal(2, payload.RefCount); // the entry, plus the one TryGet just handed us
        payload.Release();
        Assert.Equal(1, payload.RefCount);
    }

    [Fact]
    public void GetOrExecuteConsumesTheFrameOnEveryPath()
    {
        using var cache = new RespClientCache();
        var command = new FakeCommand("$5\r\nhello\r\n");

        var miss = Get("abc");
        cache.GetOrExecute(ref miss, 0, command);
        Assert.Throws<ObjectDisposedException>(() => miss.AsLookupKey());

        var hit = Get("abc");
        cache.GetOrExecute(ref hit, 0, command);
        Assert.Throws<ObjectDisposedException>(() => hit.AsLookupKey());
    }

    private static string[] KeyStrings(in RespFrame frame)
    {
        var count = frame.KeyCount;
        var ranges = new KeyRange[count];
        Assert.Equal(count, frame.TryGetKeys(ranges));
        var result = new string[count];
        for (var i = 0; i < count; i++) result[i] = Encoding.UTF8.GetString(frame.GetKey(ranges[i]).ToArray());
        return result;
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
