using System;
using System.Collections.Generic;
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

    /// <summary>Complete a fill from raw bytes; the caller's reference is released, as a real one would be.</summary>
    private static bool Complete(RespClientCache cache, in RespClientCache.RespFill fill, string response)
    {
        var payload = RespPayload.Create(Utf8(response));
        try
        {
            return cache.TryComplete(fill, payload);
        }
        finally
        {
            payload.Release();
        }
    }

    private static string Text(ReadOnlySpan<byte> value) =>
        Encoding.UTF8.GetString(value.ToArray()).Replace("\r\n", "|");

    /// <summary>Render, fill, and cache - the normal miss-then-populate path.</summary>
    private static void Fill(RespClientCache cache, string key, string response, int database = 0)
    {
        var frame = Get(key);
        Assert.True(cache.TryBeginFill(ref frame, database, out var fill));
        Assert.True(Complete(cache, fill, response));
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

        Assert.False(Complete(cache, fill, "$5\r\nstale\r\n"));
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
        Assert.True(Complete(cache, fill, "$5\r\nfresh\r\n"));
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
        Assert.True(Complete(cache, fill, "*3\r\n$1\r\n1\r\n$1\r\n2\r\n$1\r\n3\r\n"));

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

    /// <summary>An executor whose reply is a fixed blob; counts how often it was actually asked.</summary>
    private sealed class FakeExecutor(string response, Action? onSend = null) : IRespExecutor
    {
        public int Sent { get; private set; }

        public int Database => 0;

        /// <summary>Requests this executor retained, as a resending backlog would.</summary>
        public List<RespRequest> Parked { get; } = [];

        public bool ParkRequests { get; set; }

        public RespPayload Send(in RespRequest request)
        {
            Sent++;
            if (ParkRequests && request.TryRetain(out var retained)) Parked.Add(retained);
            onSend?.Invoke();
            return RespPayload.Create(Utf8(response));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    /// <summary>The ResultProcessor half: reply bytes in, result out.</summary>
    private sealed class TextHandler : IRespHandler<string>
    {
        public static readonly TextHandler Instance = new();

        public string Parse(ReadOnlySpan<byte> response) => Text(response);
    }

    [Fact]
    public void SendRunsOnceThenServesFromCache()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        for (var i = 0; i < 3; i++)
        {
            // note: no 'using' on the frame and none on any payload - Send owns both
            var frame = Get("abc");
            Assert.Equal("$5|hello|", executor.Send(ref frame, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache));
        }

        Assert.Equal(1, executor.Sent);
    }

    [Fact]
    public async Task SendAsyncMatchesSyncAndHitsCompleteSynchronously()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        var miss = Get("abc");
        Assert.Equal("$5|hello|", await executor.SendAsync(ref miss, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache));

        var hit = Get("abc");
        var pending = executor.SendAsync(ref hit, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache);

        // a hit never touches the executor, so it must not build a state machine or a Task either
        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal("$5|hello|", await pending);
        Assert.Equal(1, executor.Sent);
    }

    [Fact]
    public void ExecutorCanRetainTheRequestForAResend()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n") { ParkRequests = true };

        var frame = Get("abc");
        executor.Send(ref frame, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache);

        // this is why the request is not a span: a backlog must be able to hold it past the call, and
        // still read it afterwards to resend
        var parked = Assert.Single(executor.Parked);
        Assert.Equal("*2|$3|GET|$3|abc|", Text(parked.Span));
        parked.Dispose();
    }

    [Fact]
    public void CachedReplyIsSharedWithTheCallerNotCopied()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        var frame = Get("abc");
        executor.Send(ref frame, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache);

        using var probe = Get("abc");
        Assert.True(cache.TryGet(probe.AsLookupKey(), 0, out var payload));
        try
        {
            // the reply the executor produced IS the cached one - TryComplete retains it rather than
            // copying it into a second pooled buffer
            Assert.Equal("$5|hello|", Text(payload.Span));
        }
        finally
        {
            payload.Release();
        }
    }

    [Fact]
    public void SendWithoutACacheIsTheSameCallShape()
    {
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        var a = Get("abc");
        Assert.Equal("$5|hello|", executor.Send(ref a, TextHandler.Instance, CommandFlags.None));

        // a null cache takes the same overload, so enabling caching is one argument, not a rewrite
        var b = Get("abc");
        Assert.Equal("$5|hello|", executor.Send(ref b, TextHandler.Instance, CommandFlags.None, cache: null));

        Assert.Equal(2, executor.Sent); // no caching either way
    }

    [Fact]
    public void SendStillAnswersWhenInvalidatedInFlight()
    {
        using var cache = new RespClientCache();

        // the write lands while our command is in flight - the shape that a hand-written
        // "miss, send, then add" cannot detect, because by the add there is nothing left to compare
        var executor = new FakeExecutor("$5\r\nhello\r\n", () => cache.OnInvalidate(Utf8("abc")));

        var frame = Get("abc");
        Assert.Equal("$5|hello|", executor.Send(ref frame, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache)); // still answered
        Assert.Equal(0, cache.Count);                                                      // ... not cached
    }

    [Fact]
    public void SendAnswersEvenWhenTheFrameCannotBeCached()
    {
        using var cache = new RespClientCache();
        var writer = new RespCommandHandler(0, 70, Ctx, "MGET");
        for (var i = 0; i < 70; i++) writer.AppendFormatted((RedisKey)("k" + i));
        var frame = writer.Complete();

        var executor = new FakeExecutor("$2\r\nok\r\n");
        Assert.Equal("$2|ok|", executor.Send(ref frame, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache));
        Assert.Equal(0, cache.Count);

        // this path FALLS THROUGH to the uncached tail rather than duplicating it, so the frame must be
        // consumed there too - TryBeginFill leaves it owned when it declines
        Assert.Throws<ObjectDisposedException>(() => frame.AsLookupKey());
    }

    [Fact]
    public void SendLeavesNoReferenceBehindOnAnyPath()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        var fill = Get("abc");
        executor.Send(ref fill, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache);

        var hit = Get("abc");
        executor.Send(ref hit, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache);

        // exactly one reference survives - the cache entry's. If the helper leaked the caller's retain the
        // buffer would never return to the pool; if it over-released, the entry would be reading freed bytes
        using var probe = Get("abc"); // borrow; Detach here would own a lease nothing ever released
        Assert.True(cache.TryGet(probe.AsLookupKey(), 0, out var payload));
        Assert.Equal(2, payload.RefCount); // the entry, plus the one TryGet just handed us
        payload.Release();
        Assert.Equal(1, payload.RefCount);
    }

    [Fact]
    public void SendConsumesTheFrameOnEveryPath()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        var miss = Get("abc");
        executor.Send(ref miss, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache);
        Assert.Throws<ObjectDisposedException>(() => miss.AsLookupKey());

        var hit = Get("abc");
        executor.Send(ref hit, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache);
        Assert.Throws<ObjectDisposedException>(() => hit.AsLookupKey());

        var uncached = Get("abc");
        executor.Send(ref uncached, TextHandler.Instance, CommandFlags.None); // the no-cache overload too
        Assert.Throws<ObjectDisposedException>(() => uncached.AsLookupKey());
    }

    [Fact]
    public void KeylessCommandsAreNeverCached()
    {
        using var cache = new RespClientCache();

        // a keyless command can NEVER be invalidated: server-assisted invalidation only ever reports keys,
        // so an entry with no dependencies is vacuously valid forever. Not even a FLUSHALL clears it,
        // because OnFlush stamps key nodes and this entry has none. Permanent staleness - refuse it.
        var frame = Ctx.Execute($"{RedisCommand.TIME}");
        Assert.Equal(0, frame.KeyCount);
        Assert.False(cache.TryBeginFill(ref frame, 0, out _));
        frame.Dispose();

        Assert.Equal(0, cache.Count);
    }

    [Theory]
    // cacheable: a declared category no more severe than read-only
    [InlineData(CommandFlags.CommandRetryAlways, true)]
    [InlineData(CommandFlags.CommandRetryConnection, true)]
    [InlineData(CommandFlags.CommandRetryReadOnly, true)]
    // not cacheable: writes and above
    [InlineData(CommandFlags.CommandRetryWriteChecked, false)]
    [InlineData(CommandFlags.CommandRetryWriteLastWins, false)]
    [InlineData(CommandFlags.CommandRetryWriteAccumulating, false)]
    [InlineData(CommandFlags.CommandRetryServerAdmin, false)]
    [InlineData(CommandFlags.CommandRetryNever, false)]
    // and the trap: nobody declared one. Zero sits BELOW read-only on the ladder, so a naive <= test
    // would read "nobody said" as "safe to cache" - backwards, and exactly the case that matters for
    // commands this library does not know, such as NRedisStack's FT.*
    [InlineData(CommandFlags.None, false)]
    public void CachingDemandsADeclaredReadOnlyCategory(CommandFlags flags, bool cacheable)
    {
        using var cache = new RespClientCache();
        var frame = Get("abc");
        Assert.Equal(cacheable, cache.TryBeginFill(ref frame, 0, flags, out var fill));

        if (cacheable)
        {
            Assert.True(Complete(cache, fill, "$5\r\nhello\r\n"));
        }
        else
        {
            frame.Dispose();
        }
    }

    [Fact]
    public void UnsetCategoryIsRefusedEvenThoughItComparesBelowReadOnly()
    {
        // pinning the arithmetic directly, because this is the one that fails open if written naively
        Assert.True(RespClientCache.IsCacheable(CommandFlags.CommandRetryReadOnly));
        Assert.False(RespClientCache.IsCacheable(CommandFlags.None));
        Assert.True((CommandFlags.None & Message.MaskRetryCategory) < CommandFlags.CommandRetryReadOnly);

        // flags unrelated to the category must not accidentally satisfy the gate
        Assert.False(RespClientCache.IsCacheable(CommandFlags.PreferReplica | CommandFlags.FireAndForget));
    }

    [Fact]
    public void NoClientCacheSuppressesStoring()
    {
        using var cache = new RespClientCache();
        var frame = Get("abc");

        Assert.False(cache.TryBeginFill(
            ref frame, 0, CommandFlags.CommandRetryReadOnly | CommandFlags.NoClientCache, out _));
        frame.Dispose();

        Assert.Equal(0, cache.Count);
        Assert.Equal(1, cache.RefusedByFlags);
    }

    [Fact]
    public void NoClientCacheAlsoSuppressesServingFromCache()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        var fill = Get("abc");
        executor.Send(ref fill, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache);
        Assert.Equal(1, executor.Sent);

        // opting out must mean the caller does not RECEIVE a cached answer either - not merely that this
        // reply is not kept. Otherwise "don't cache this" silently still serves stale data.
        var opted = Get("abc");
        executor.Send(ref opted, TextHandler.Instance,
            CommandFlags.CommandRetryReadOnly | CommandFlags.NoClientCache, cache);
        Assert.Equal(2, executor.Sent);

        // ... and the entry is untouched for callers who did not opt out
        var normal = Get("abc");
        executor.Send(ref normal, TextHandler.Instance, CommandFlags.CommandRetryReadOnly, cache);
        Assert.Equal(2, executor.Sent);
    }

    [Fact]
    public void RefusalCountersSayWhyNothingWasCached()
    {
        using var cache = new RespClientCache();

        var undeclared = Get("abc");
        cache.TryBeginFill(ref undeclared, 0, CommandFlags.None, out _);
        undeclared.Dispose();

        var keyless = Ctx.Execute($"{RedisCommand.TIME}");
        cache.TryBeginFill(ref keyless, 0, CommandFlags.CommandRetryReadOnly, out _);
        keyless.Dispose();

        var raced = Get("xyz");
        Assert.True(cache.TryBeginFill(ref raced, 0, CommandFlags.CommandRetryReadOnly, out var fill));
        cache.OnInvalidate(Utf8("xyz"));
        Assert.False(Complete(cache, fill, "$1\r\nx\r\n"));

        var good = Get("ok");
        Assert.True(cache.TryBeginFill(ref good, 0, CommandFlags.CommandRetryReadOnly, out var ok));
        Assert.True(Complete(cache, ok, "$1\r\nx\r\n"));

        // the silent failure this design can still produce is a durable one, so each refusal reason is
        // separately countable rather than lumped into "it didn't cache"
        Assert.Equal(1, cache.RefusedByFlags);
        Assert.Equal(1, cache.RefusedNoKeys);
        Assert.Equal(1, cache.RefusedRaced);
        Assert.Equal(1, cache.Stored);
    }

    [Fact]
    public void NoClientCacheIsUserSelectable()
    {
        // an external surface has to be able to pass it through Execute, or the opt-out is unreachable
        // for exactly the callers who need it
        Assert.Equal(
            CommandFlags.NoClientCache,
            Message.UserSelectableFlags & CommandFlags.NoClientCache);
    }

    [Fact]
    public void RedundantFillsCountConcurrentMissesOnTheSameRequest()
    {
        using var cache = new RespClientCache();

        // two callers miss on the same request and both go to the server - exactly what request
        // combining would have collapsed into one round trip
        var first = Get("abc");
        Assert.True(cache.TryBeginFill(ref first, 0, CommandFlags.CommandRetryReadOnly, out var a));
        var second = Get("abc");
        Assert.True(cache.TryBeginFill(ref second, 0, CommandFlags.CommandRetryReadOnly, out var b));

        Assert.True(Complete(cache, a, "$5\r\nhello\r\n"));
        Assert.False(Complete(cache, b, "$5\r\nhello\r\n")); // lost the race; the first entry stands

        Assert.Equal(1, cache.Stored);
        Assert.Equal(1, cache.RedundantFills);
        Assert.Equal(1, cache.Count);
    }

    [Theory]
    [InlineData("$-1\r\n")]   // RESP2 null bulk string
    [InlineData("*-1\r\n")]   // RESP2 null array - a different spelling, still a value
    [InlineData("_\r\n")]     // RESP3 null
    public void NullRepliesAreCached(string reply)
    {
        using var cache = new RespClientCache();
        var frame = Get("missing");
        Assert.True(cache.TryBeginFill(ref frame, 0, CommandFlags.CommandRetryReadOnly, out var fill));

        // a null is a VALUE, not a failure, in all three spellings. Redis tracks every key "mentioned in
        // the context of a read-only command", found or not, so creating the key invalidates this entry -
        // negative caching that is actually correct. Only '-' and '!' are errors, and no null starts with
        // either, so the cheap first-byte test does not need to enumerate the null forms.
        Assert.True(Complete(cache, fill, reply));
        Assert.True(TryRead(cache, "missing", out var text));
        Assert.Equal(reply.Replace("\r\n", "|"), text);

        Assert.True(cache.OnInvalidate(Utf8("missing")));
        Assert.False(TryRead(cache, "missing", out _));
        Assert.Equal(0, cache.RefusedError);
    }

    [Theory]
    [InlineData("-ERR something went wrong\r\n")]
    [InlineData("-WRONGTYPE Operation against a key holding the wrong kind of value\r\n")]
    [InlineData("-MOVED 1234 127.0.0.1:7001\r\n")]
    [InlineData("!21\r\nSYNTAX invalid syntax\r\n")]
    public void ErrorRepliesAreNotCached(string reply)
    {
        using var cache = new RespClientCache();
        var frame = Get("abc");
        Assert.True(cache.TryBeginFill(ref frame, 0, CommandFlags.CommandRetryReadOnly, out var fill));

        // an error is not necessarily a function of the tracked keys - it can depend on server config,
        // topology, ACLs, a module's state - so the invariant that makes this cache sound does not hold
        // for it, and nothing may ever invalidate it. Caching one turns a transient failure permanent.
        Assert.False(Complete(cache, fill, reply));
        Assert.Equal(0, cache.Count);
        Assert.Equal(1, cache.RefusedError);
    }

    // RESP3 attribute metadata: |1 <key> <value>, which may legally precede any reply
    private const string Attribute = "|1\r\n$6\r\nttl-ms\r\n:1000\r\n";

    [Theory]
    [InlineData(Attribute + "-ERR something went wrong\r\n")]
    [InlineData(Attribute + "!21\r\nSYNTAX invalid syntax\r\n")]
    public void ErrorsBehindLeadingAttributesAreStillRefused(string reply)
    {
        using var cache = new RespClientCache();
        var frame = Get("abc");
        Assert.True(cache.TryBeginFill(ref frame, 0, CommandFlags.CommandRetryReadOnly, out var fill));

        // a first-byte test would see '|' and cache the error behind it. No server is known to emit
        // attributes today, which is precisely why this would have gone unnoticed.
        Assert.False(Complete(cache, fill, reply));
        Assert.Equal(0, cache.Count);
        Assert.Equal(1, cache.RefusedError);
    }

    [Fact]
    public void ValuesBehindLeadingAttributesAreStillCached()
    {
        using var cache = new RespClientCache();
        var frame = Get("abc");
        Assert.True(cache.TryBeginFill(ref frame, 0, CommandFlags.CommandRetryReadOnly, out var fill));

        Assert.True(Complete(cache, fill, Attribute + "$5\r\nhello\r\n"));
        Assert.Equal(1, cache.Stored);
        Assert.Equal(0, cache.RefusedError);
    }

    [Fact]
    public void RepliesWithNoContentElementAreRefused()
    {
        using var cache = new RespClientCache();
        var frame = Get("abc");
        Assert.True(cache.TryBeginFill(ref frame, 0, CommandFlags.CommandRetryReadOnly, out var fill));

        // metadata and nothing else: cannot be classified, so it fails closed rather than being stored
        Assert.False(Complete(cache, fill, Attribute));
        Assert.Equal(0, cache.Count);
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
        Assert.True(Complete(cache, fill, "*2\r\n$1\r\n1\r\n$1\r\n2\r\n"));

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
