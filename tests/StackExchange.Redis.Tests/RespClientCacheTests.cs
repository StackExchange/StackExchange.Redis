using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interpolated;
using Xunit;
using RESPite.Messages;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The two-table client-side cache: (frame, db) => (payload, generations), and key => generation. No server
/// involved - invalidation is poked in from outside, exactly as a push handler would.
/// </summary>
public class RespClientCacheTests
{
    private static readonly RespContext Ctx = new();

    private static RespContext Via(IRespExecutor executor, RespClientCache? cache = null)
        => new RespContext().WithExecutor(executor).WithCache(cache);

    private static RespFrame Get(string key) => Ctx.Render($"{RedisCommand.GET}{(RedisKey)key}");

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
        cache.OnInvalidate(hit);

        // BCAST hands us every key touched on the server; this path must not allocate at all
        AllocationAssert.None(() => cache.OnInvalidate(miss), iterations: 10_000);
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

        var frame = Ctx.Render($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}{(RedisKey)"c"}");
        Assert.True(frame.KeysNeedScan);  // beyond the two inline offsets: resolved from the bitmap
        Assert.Equal(3, frame.KeyCount);
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.True(Complete(cache, fill, "*3\r\n$1\r\n1\r\n$1\r\n2\r\n$1\r\n3\r\n"));

        Assert.True(ThreeKeyHit(cache));
        Assert.True(cache.OnInvalidate(Utf8(((char)('a' + which)).ToString())));
        Assert.False(ThreeKeyHit(cache));

        static bool ThreeKeyHit(RespClientCache cache)
        {
            using var probe = Ctx.Render($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}{(RedisKey)"c"}");
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
        using var two = Ctx.Render($"{RedisCommand.MGET}{(RedisKey)"alpha"}{(RedisKey)"beta"}");
        Assert.False(two.KeysNeedScan);
        Assert.Equal(new[] { "alpha", "beta" }, KeyStrings(two));

        using var three = Ctx.Render($"{RedisCommand.MGET}{(RedisKey)"alpha"}{(RedisKey)"beta"}{(RedisKey)"gamma"}");
        Assert.True(three.KeysNeedScan);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, KeyStrings(three));
    }

    [Fact]
    public void KeysAreFoundAmongNonKeyArguments()
    {
        // the bitmap indexes ARGUMENTS, so values interleaved with keys must not shift the walk
        using var frame = Ctx.Render(
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
        using var frame = Ctx.Render($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}{(RedisKey)"c"}");
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

            // a real executor captures no reply for fire-and-forget: the caller declined it. Faking one
            // would make every assertion about that flag meaningless.
            return (request.Flags & CommandFlags.FireAndForget) != 0
                ? null!
                : RespPayload.Create(Utf8(response));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    /// <summary>The ResultProcessor half: reply bytes in, result out.</summary>
    /// <remarks>
    /// Takes the <b>payload</b> rather than a positioned reader, deliberately: these tests assert that a
    /// cache hit hands back the identical frame - prefix, length and all - so the handler has to see what a
    /// reader has already moved past. Decoding the value instead would still pass, and would stop proving
    /// the thing the file exists to prove.
    /// </remarks>
    private sealed class TextHandler : IRespPayloadHandler<string>
    {
        public static readonly TextHandler Instance = new();

        public string Parse(RespPayload payload) => Text(payload.Span);

        string IRespHandler<string>.Parse(ref RespReader reader)
            => throw new NotSupportedException("this handler wants the raw frame; callers take the payload form");
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
            Assert.Equal("$5|hello|", Via(executor, cache).Send(ref frame, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default));
        }

        Assert.Equal(1, executor.Sent);
    }

    [Fact]
    public async Task SendAsyncMatchesSyncAndHitsCompleteSynchronously()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        var miss = Get("abc");
        Assert.Equal("$5|hello|", await Via(executor, cache).SendAsync(ref miss, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default));

        var hit = Get("abc");
        var pending = Via(executor, cache).SendAsync(ref hit, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);

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
        Via(executor, cache).Send(ref frame, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);

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
        Via(executor, cache).Send(ref frame, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);

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
        Assert.Equal("$5|hello|", Via(executor).Send(ref a, CommandFlags.None, TextHandler.Instance, default));

        // a null cache takes the same overload, so enabling caching is one argument, not a rewrite
        var b = Get("abc");
        Assert.Equal("$5|hello|", Via(executor).Send(ref b, CommandFlags.None, TextHandler.Instance, default));

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
        Assert.Equal("$5|hello|", Via(executor, cache).Send(ref frame, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default));
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
        Assert.Equal("$2|ok|", Via(executor, cache).Send(ref frame, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default));
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
        Via(executor, cache).Send(ref fill, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);

        var hit = Get("abc");
        Via(executor, cache).Send(ref hit, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);

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
        Via(executor, cache).Send(ref miss, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);
        Assert.Throws<ObjectDisposedException>(() => miss.AsLookupKey());

        var hit = Get("abc");
        Via(executor, cache).Send(ref hit, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);
        Assert.Throws<ObjectDisposedException>(() => hit.AsLookupKey());

        var uncached = Get("abc");
        Via(executor).Send(ref uncached, CommandFlags.None, TextHandler.Instance, default); // the no-cache overload too
        Assert.Throws<ObjectDisposedException>(() => uncached.AsLookupKey());
    }

    [Fact]
    public void KeylessCommandsAreNeverCached()
    {
        using var cache = new RespClientCache();

        // a keyless command can NEVER be invalidated: server-assisted invalidation only ever reports keys,
        // so an entry with no dependencies is vacuously valid forever. Not even a FLUSHALL clears it,
        // because OnFlush stamps key nodes and this entry has none. Permanent staleness - refuse it.
        var frame = Ctx.Render($"{RedisCommand.TIME}");
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
        Via(executor, cache).Send(ref fill, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);
        Assert.Equal(1, executor.Sent);

        // opting out must mean the caller does not RECEIVE a cached answer either - not merely that this
        // reply is not kept. Otherwise "don't cache this" silently still serves stale data.
        var opted = Get("abc");
        Via(executor, cache).Send(ref opted, CommandFlags.CommandRetryReadOnly | CommandFlags.NoClientCache, TextHandler.Instance, default);
        Assert.Equal(2, executor.Sent);

        // ... and the entry is untouched for callers who did not opt out
        var normal = Get("abc");
        Via(executor, cache).Send(ref normal, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);
        Assert.Equal(2, executor.Sent);
    }

    [Fact]
    public void RefusalCountersSayWhyNothingWasCached()
    {
        using var cache = new RespClientCache();

        var undeclared = Get("abc");
        cache.TryBeginFill(ref undeclared, 0, CommandFlags.None, out _);
        undeclared.Dispose();

        var keyless = Ctx.Render($"{RedisCommand.TIME}");
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

    [Fact]
    public void ADetachedRequestCanStillAnswerForItself()
    {
        // the point of the widening: an executor decorator sees a RespRequest and nothing else, so the
        // request has to carry what routing, retry and caching each need to ask
        using var frame = Ctx.Render($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}{(RedisKey)"c"}");
        using var request = frame.Detach(CommandFlags.CommandRetryReadOnly | CommandFlags.NoClientCache);

        Assert.Equal(4, request.ArgCount);
        Assert.Equal(CommandFlags.CommandRetryReadOnly | CommandFlags.NoClientCache, request.Flags);
        Assert.Equal(3, request.KeyCount);

        var ranges = new KeyRange[request.KeyCount];
        Assert.Equal(3, request.TryGetKeys(ranges));
        Assert.Equal(
            new[] { "a", "b", "c" },
            ranges.Select(r => Encoding.UTF8.GetString(request.GetKey(r).ToArray())).ToArray());
    }

    [Fact]
    public void RoutingNeedsOnlyTheSlotAndTheRequestCarriesIt()
    {
        var cluster = new RespContext(serverType: ServerType.Cluster);
        using var frame = cluster.Render($"{RedisCommand.GET}{(RedisKey)"{tag}:x"}");
        var slot = frame.Slot;
        Assert.NotEqual(ServerSelectionStrategy.NoSlot, slot);

        using var request = frame.Detach();
        Assert.Equal(slot, request.Slot); // one int; no key marks involved in routing at all
    }

    [Fact]
    public void MetadataDoesNotAffectRequestIdentity()
    {
        using var plain = Get("abc").Detach(CommandFlags.CommandRetryReadOnly);
        using var different = Get("abc").Detach(CommandFlags.CommandRetryNever | CommandFlags.NoClientCache);

        // identity is the rendered bytes and only the rendered bytes: two callers issuing the same command
        // with different flags are asking the same question, so they must share a cache entry
        Assert.Equal(plain, different);
        Assert.Equal(plain.GetHashCode(), different.GetHashCode());
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

        var frame = Ctx.Render($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}");
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.True(Complete(cache, fill, "*2\r\n$1\r\n1\r\n$1\r\n2\r\n"));

        using (var probe = Ctx.Render($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}"))
        {
            Assert.True(cache.TryGet(probe.AsLookupKey(), 0, out var payload));
            payload.Release();
        }

        cache.OnInvalidate(Utf8("b")); // the SECOND key

        using (var probe = Ctx.Render($"{RedisCommand.MGET}{(RedisKey)"a"}{(RedisKey)"b"}"))
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
    /// <summary>
    /// With <c>PREFIX</c> in play, a key outside the tracked set is refused rather than cached.
    /// </summary>
    /// <remarks>
    /// Under <c>BCAST</c> the server announces only keys matching a prefix, so an entry outside the set has
    /// nothing that will ever say it is wrong: it would be served until the lifetime alone retired it. That
    /// is the same defect as caching a keyless reply, and it gets the same answer.
    /// </remarks>
    [Theory]
    [InlineData("app:user:1", true)]
    [InlineData("app:", true)]      // the prefix itself is inside the set
    [InlineData("apple", false)]    // shares a leading "app" but not the prefix
    [InlineData("other:1", false)]
    [InlineData("", false)]
    public void UntrackedKeysAreNotCached(string key, bool cacheable)
    {
        using var cache = new RespClientCache(new CacheOptions { Prefixes = ["app:", "session:"] });

        var frame = Ctx.Render($"{RedisCommand.GET}{(RedisKey)key}");
        var admitted = cache.TryBeginFill(ref frame, 0, out var fill);
        Assert.Equal(cacheable, admitted);
        if (admitted)
        {
            Assert.True(Complete(cache, fill, "$1\r\nx\r\n"));
        }
        else
        {
            frame.Dispose();
        }

        Assert.Equal(cacheable ? 0 : 1, cache.RefusedNotTracked);
        Assert.Equal(cacheable ? 1 : 0, cache.Count);
    }

    /// <summary>
    /// Every key must be tracked, not merely one of them.
    /// </summary>
    /// <remarks>
    /// The entry depends on all of its keys, so one key the server was never asked to watch is enough to
    /// make the whole reply uninvalidatable - a write to it would go unannounced and the reply would go on
    /// being served. "Mostly invalidatable" is not a thing.
    /// </remarks>
    [Fact]
    public void OneUntrackedKeySpoilsAMultiKeyCommand()
    {
        using var cache = new RespClientCache(new CacheOptions { Prefixes = ["app:"] });

        var frame = Ctx.Render($"{RedisCommand.MGET}{(RedisKey)"app:a"}{(RedisKey)"app:b"}{(RedisKey)"other"}");
        Assert.False(cache.TryBeginFill(ref frame, 0, out _));
        frame.Dispose();
        Assert.Equal(1, cache.RefusedNotTracked);

        // ...and the same command with every key inside the set is fine
        var ok = Ctx.Render($"{RedisCommand.MGET}{(RedisKey)"app:a"}{(RedisKey)"app:b"}{(RedisKey)"app:c"}");
        Assert.True(cache.TryBeginFill(ref ok, 0, out var fill));
        Assert.True(Complete(cache, fill, "*3\r\n$1\r\n1\r\n$1\r\n2\r\n$1\r\n3\r\n"));
    }

    /// <summary>An empty prefix list means "track everything", so nothing is refused for being outside it.</summary>
    [Fact]
    public void NoPrefixesMeansEverythingIsCacheable()
    {
        using var cache = new RespClientCache(new CacheOptions { DefaultPolicy = new CachePolicy() }); // the default: BCAST with no prefix

        var frame = Ctx.Render($"{RedisCommand.GET}{(RedisKey)"anything at all"}");
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.True(Complete(cache, fill, "$1\r\nx\r\n"));
        Assert.Equal(0, cache.RefusedNotTracked);
    }

    /// <summary>
    /// An empty prefix is rejected rather than treated as "everything".
    /// </summary>
    /// <remarks>
    /// "" matches every key, so accepting it would silently turn a deliberately narrow list into a total
    /// one - the failure mode being a cache that looks scoped and is not. An empty <i>list</i> already says
    /// "everything", unambiguously.
    /// </remarks>
    [Fact]
    public void AnEmptyPrefixIsRejected()
    {
        // ALONE, and checked by message. Paired with a real prefix it is caught by the overlap rule
        // instead - every string starts with "" - so that spelling passes even with this rule deleted,
        // which is exactly what it did until a mutant walked through it.
        var ex = Assert.Throws<ArgumentException>(() => new CacheOptions { Prefixes = [""] });
        Assert.Contains("matches every key", ex.Message);

        Assert.Throws<ArgumentException>(() => new CacheOptions { Prefixes = ["app:", ""] });
    }

    /// <summary>
    /// Overlapping prefixes are rejected here, because the server rejects them there.
    /// </summary>
    /// <remarks>
    /// <c>CLIENT TRACKING</c> refuses a prefix list where one entry is a prefix of another. Catching it at
    /// construction puts the failure where the mistake was made rather than in a handshake much later.
    /// </remarks>
    [Fact]
    public void OverlappingPrefixesAreRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => new CacheOptions { Prefixes = ["app:", "app:user:"] });
        Assert.Contains("must not overlap", ex.Message);

        // ...including a prefix repeated, which overlaps itself in the most literal way available
        Assert.Throws<ArgumentException>(() => new CacheOptions { Prefixes = ["app:", "app:"] });
    }

    /// <summary>Prefix matching is on the bytes, so a multi-byte prefix is not matched by accident.</summary>
    [Fact]
    public void PrefixesMatchWholeBytesNotCharacters()
    {
        using var cache = new RespClientCache(new CacheOptions { Prefixes = ["é:"] }); // 0xC3 0xA9

        // a key starting with the first byte of the prefix but not the second must not match
        var frame = Ctx.Render($"{RedisCommand.GET}{(RedisKey)"è:x"}"); // 0xC3 0xA8
        Assert.False(cache.TryBeginFill(ref frame, 0, out _));
        frame.Dispose();

        var ok = Ctx.Render($"{RedisCommand.GET}{(RedisKey)"é:x"}");
        Assert.True(cache.TryBeginFill(ref ok, 0, out var fill));
        Assert.True(Complete(cache, fill, "$1\r\nx\r\n"));
    }

    /// <summary>
    /// A fire-and-forget read is not cached, and - the part that matters - is not <i>served</i> from cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fire-and-forget promises the caller <c>default</c>. A cache hit would hand back a real value, so the
    /// same call would answer differently depending on whether something else had happened to read that key
    /// first. A cache may make a call faster; it may not make it return something else.
    /// </para>
    /// <para>
    /// The store side is merely impossible rather than wrong - no reply is observed, so there is nothing to
    /// keep and no way to check it was not an error. Which disposes of the one coherent reading of
    /// "fire-and-forget read": warming the cache.
    /// </para>
    /// </remarks>
    [Fact]
    public void FireAndForgetIsNeitherCachedNorServed()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        const CommandFlags FireAndForget = CommandFlags.CommandRetryReadOnly | CommandFlags.FireAndForget;

        // nothing stored, and the executor was still asked: the command really was sent
        var frame = Get("abc");
        Assert.Null(Via(executor, cache).Send(ref frame, FireAndForget, TextHandler.Instance, default));
        Assert.Equal(1, executor.Sent);
        Assert.Equal(0, cache.Count);
        Assert.Equal(1, cache.RefusedByFlags);

        // now cache it properly, so there IS something a probe could wrongly return
        var warm = Get("abc");
        Assert.Equal("$5|hello|", Via(executor, cache).Send(ref warm, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default));
        Assert.Equal(1, cache.Count);

        // ...and the fire-and-forget caller still gets default, not the cached value
        var again = Get("abc");
        Assert.Null(Via(executor, cache).Send(ref again, FireAndForget, TextHandler.Instance, default));
        Assert.Equal(3, executor.Sent);
    }

    /// <summary>The asynchronous path agrees with the synchronous one, including on the hit that isn't.</summary>
    [Fact]
    public async Task FireAndForgetIsNotServedAsynchronouslyEither()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        const CommandFlags FireAndForget = CommandFlags.CommandRetryReadOnly | CommandFlags.FireAndForget;

        var warm = Get("abc");
        Assert.Equal("$5|hello|", await Via(executor, cache).SendAsync(ref warm, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default));
        Assert.Equal(1, cache.Count);

        var ff = Get("abc");
        Assert.Null(await Via(executor, cache).SendAsync(ref ff, FireAndForget, TextHandler.Instance, default));
        Assert.Equal(2, executor.Sent);
        Assert.Equal(1, cache.Count); // and it did not disturb what was already there
    }

    /// <summary>A reply larger than the limit is refused, and one at the limit is kept.</summary>
    /// <remarks>
    /// The cheapest bound there is: the reply's size is known before anything is stored, so this costs no
    /// bookkeeping at all. Boundary included deliberately - "larger than" and "at least" differ by exactly
    /// the case a limit is most often written wrongly for.
    /// </remarks>
    [Theory]
    [InlineData(4, false)]  // "$1\r\nx\r\n" is 7 bytes
    [InlineData(7, true)]
    [InlineData(8, true)]
    public void RepliesOverTheSizeLimitAreRefused(int limit, bool cacheable)
    {
        using var cache = new RespClientCache(new CacheOptions { MaxPayloadBytes = limit });

        var frame = Get("abc");
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.Equal(cacheable, Complete(cache, fill, "$1\r\nx\r\n"));
        Assert.Equal(cacheable ? 1 : 0, cache.Count);
        Assert.Equal(cacheable ? 0 : 1, cache.RefusedTooLarge);
    }

    /// <summary>With no limit set, size is not a reason to refuse.</summary>
    [Fact]
    public void NoSizeLimitMeansNoSizeRefusals()
    {
        using var cache = new RespClientCache(new CacheOptions { MaxPayloadBytes = null });

        var frame = Get("abc");
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.True(Complete(cache, fill, "$1\r\nx\r\n"));
        Assert.Equal(0, cache.RefusedTooLarge);
    }

    /// <summary>A size limit must be positive; null is how you say "no limit".</summary>
    [Fact]
    public void ANonPositiveSizeLimitIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CacheOptions { MaxPayloadBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CacheOptions { MaxPayloadBytes = -1 });
    }

    /// <summary>
    /// Sweeping reclaims entries that merely <b>expired</b>, not only invalidated ones.
    /// </summary>
    /// <remarks>
    /// Expiry is decided when an entry is read, so reading is what refuses it - and for a key nothing ever
    /// comes back for, nothing ever reads it, so nothing ever removes it. That is precisely the entry a
    /// lifetime cannot help with, because nobody is there to notice it has passed.
    /// </remarks>
    [Fact]
    public async Task SweepReclaimsExpiredEntriesAndNotOnlyInvalidatedOnes()
    {
        using var cache = new RespClientCache(new CacheOptions
        {
            DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromMilliseconds(30) },
        });

        var frame = Get("abc");
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.True(Complete(cache, fill, "$1\r\nx\r\n"));
        Assert.Equal(1, cache.Count);

        // still live: nothing to reclaim, and nobody has invalidated it
        Assert.Equal(0, cache.Sweep());
        Assert.Equal(1, cache.Count);

        await Task.Delay(80);
        Assert.Equal(1, cache.Sweep());
        Assert.Equal(0, cache.Count);
    }

    /// <summary>A sweep that is not yet due does nothing; one that is, sweeps.</summary>
    [Fact]
    public async Task SweepIfDueHonoursTheInterval()
    {
        using var cache = new RespClientCache(new CacheOptions
        {
            SweepInterval = TimeSpan.FromMilliseconds(50),
            DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromMilliseconds(10) },
        });

        var frame = Get("abc");
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.True(Complete(cache, fill, "$1\r\nx\r\n"));

        await Task.Delay(20); // expired, but the sweep is not due yet
        Assert.Equal(0, cache.SweepIfDue());
        Assert.Equal(1, cache.Count);

        await Task.Delay(60);
        Assert.Equal(1, cache.SweepIfDue());
        Assert.Equal(0, cache.Count);
    }

    /// <summary>Sweeping can be turned off entirely.</summary>
    [Fact]
    public async Task ASweepIntervalOfZeroNeverSweeps()
    {
        using var cache = new RespClientCache(new CacheOptions
        {
            SweepInterval = TimeSpan.Zero,
            DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromMilliseconds(10) },
        });

        var frame = Get("abc");
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.True(Complete(cache, fill, "$1\r\nx\r\n"));

        await Task.Delay(40);
        Assert.Equal(0, cache.SweepIfDue());
        Assert.Equal(1, cache.Count);

        // ...but an explicit sweep still works: the interval governs the driver, not the operation
        Assert.Equal(1, cache.Sweep());
    }

    /// <summary>
    /// Concurrent sweeps reclaim every entry exactly once, and never twice.
    /// </summary>
    /// <remarks>
    /// <b>What this can and cannot pin.</b> The compare-exchange in <c>SweepIfDue</c> is there so two
    /// drivers arriving together do one sweep rather than two - but that is a cost property, and the window
    /// is microseconds wide, so asserting the collapse would be asserting a race. A mutant that claimed the
    /// timestamp after the work instead of before duly survived that assertion. What is worth pinning, and
    /// is deterministic, is the safety underneath it: whatever the interleaving, each entry is removed by
    /// exactly one caller and disposed exactly once - a double release would return a live buffer to the
    /// pool, which is the failure that actually costs something.
    /// </remarks>
    [Fact]
    public async Task ConcurrentSweepsReclaimEachEntryExactlyOnce()
    {
        using var cache = new RespClientCache(new CacheOptions
        {
            SweepInterval = TimeSpan.FromMilliseconds(10),
            DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromMilliseconds(5) },
        });

        for (var i = 0; i < 20; i++)
        {
            var frame = Get("key" + i);
            Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
            Assert.True(Complete(cache, fill, "$1\r\nx\r\n"));
        }

        await Task.Delay(40);

        var tasks = new Task<int>[8];
        for (var i = 0; i < tasks.Length; i++) tasks[i] = Task.Run(cache.SweepIfDue);
        var removed = await Task.WhenAll(tasks);

        Assert.Equal(20, removed.Sum()); // every entry reclaimed, and none of them twice
        Assert.Equal(0, cache.Count);
    }

    /// <summary>
    /// Prefixes require broadcast: under per-key tracking there is nothing to filter.
    /// </summary>
    /// <remarks>
    /// Raised when the cache is built rather than from an <c>init</c> accessor, because an object
    /// initializer assigns in whatever order the caller wrote it - so a rule spanning two properties would
    /// pass or fail on line ordering. The message names both halves, since either one could be the mistake.
    /// </remarks>
    [Fact]
    public void PrefixesRequireBroadcastTracking()
    {
        var options = new CacheOptions
        {
            TrackingMode = CacheTrackingMode.PerKey,
            Prefixes = ["app:"],
        };

        var ex = Assert.Throws<ArgumentException>(() => new RespClientCache(options));
        Assert.Contains("Broadcast", ex.Message);

        // ...and the same list is fine the other way round, whichever order it was written in
        using var ok = new RespClientCache(new CacheOptions { Prefixes = ["app:"], TrackingMode = CacheTrackingMode.Broadcast });
        using var alsoOk = new RespClientCache(new CacheOptions { TrackingMode = CacheTrackingMode.Broadcast, Prefixes = ["app:"] });
    }

    /// <summary>
    /// Under per-key tracking every key we read is tracked by definition, so nothing is refused for being
    /// outside a set.
    /// </summary>
    [Fact]
    public void PerKeyTrackingCachesAnyKey()
    {
        using var cache = new RespClientCache(new CacheOptions { TrackingMode = CacheTrackingMode.PerKey });

        var frame = Get("anything at all");
        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        Assert.True(Complete(cache, fill, "$1\r\nx\r\n"));
        Assert.Equal(0, cache.RefusedNotTracked);
    }

    /// <summary>Fill a cache with <paramref name="count"/> distinct single-key entries.</summary>
    private static void Fill(RespClientCache cache, int count, string response = "$1\r\nx\r\n")
    {
        for (var i = 0; i < count; i++)
        {
            var frame = Get("key" + i);
            if (cache.TryBeginFill(ref frame, 0, out var fill))
            {
                Complete(cache, fill, response);
            }
            else
            {
                frame.Dispose();
            }
        }
    }

    /// <summary>An entry limit is honoured, by evicting rather than by refusing.</summary>
    /// <remarks>
    /// Eviction runs after the store, so the budget is a target the cache returns to rather than a wall:
    /// refusing instead would throw away a reply already paid for in full, having no idea yet whether it
    /// was worth more than what is already held.
    /// </remarks>
    [Fact]
    public void AnEntryLimitEvictsDownToSize()
    {
        using var cache = new RespClientCache(new CacheOptions { MaxEntries = 10 });

        Fill(cache, 50);

        Assert.Equal(10, cache.Count);
        Assert.Equal(50, cache.Stored);   // everything really was stored...
        Assert.Equal(40, cache.Evicted);  // ...and the excess evicted, not refused
    }

    /// <summary>A byte budget is honoured, and counts what entries hold rather than what they carry.</summary>
    [Fact]
    public void AByteBudgetEvictsDownToSize()
    {
        // sizing matters here: a 7-byte reply is rented from the pool's 16-byte bucket, so 200 of them
        // hold ~3.2KB. A budget above that would be tested by a cache that never reached it.
        using var cache = new RespClientCache(new CacheOptions { MaxBytes = 512 });

        Fill(cache, 200);

        Assert.True(cache.Bytes <= 512, $"over budget: {cache.Bytes}");
        Assert.True(cache.Count > 0, "evicted everything");
        Assert.True(cache.Evicted > 0, "nothing was evicted");
    }

    /// <summary>The byte count tracks stores, evictions, sweeps and disposal alike.</summary>
    /// <remarks>
    /// Drift here is the failure that makes a budget useless without looking broken: a count that only ever
    /// rises stops admitting anything, and one that only ever falls stops binding. Both are silent.
    /// </remarks>
    [Fact]
    public void TheByteCountReturnsToZeroWhenEverythingGoes()
    {
        var cache = new RespClientCache();
        try
        {
            Assert.Equal(0, cache.Bytes);
            Fill(cache, 20);
            Assert.True(cache.Bytes > 0);

            // invalidate half, and sweep them
            for (var i = 0; i < 10; i++) cache.OnInvalidate(Utf8("key" + i));
            Assert.Equal(10, cache.Sweep());
            Assert.Equal(10, cache.Count);
            Assert.True(cache.Bytes > 0);
        }
        finally
        {
            cache.Dispose();
        }

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
    }

    /// <summary>A refresh replacing an entry adjusts the budget by the difference, not by the whole.</summary>
    /// <remarks>
    /// The replace path swaps the value in place rather than removing and re-adding, so it is the one store
    /// that has to credit the old entry itself. Getting it wrong leaks budget on every refresh, which shows
    /// up only after a cache has been running for a while - the worst kind of bug to go looking for.
    /// </remarks>
    [Fact]
    public async Task ARefreshAdjustsTheBudgetByTheDifference()
    {
        using var cache = new RespClientCache(new CacheOptions
        {
            DefaultPolicy = new CachePolicy
            {
                RefreshAfter = TimeSpan.FromMilliseconds(40),
                TimeToLive = TimeSpan.FromMinutes(5),
            },
        });
        var executor = new FakeExecutor("$1\r\nx\r\n");
        var context = Via(executor, cache);

        var frame = Get("abc");
        Assert.Equal("$1|x|", await context.SendAsync(ref frame, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default));
        var first = cache.Bytes;
        Assert.True(first > 0);

        await Task.Delay(90); // past the soft threshold: the next read is served AND starts a refresh
        var again = Get("abc");
        Assert.Equal("$1|x|", await context.SendAsync(ref again, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default));

        Assert.True(
            await WaitUntil(() => cache.Stored == 2),
            $"the refresh never landed: stored={cache.Stored} refreshes={cache.Refreshes}");

        Assert.Equal(1, cache.Count);
        Assert.Equal(first, cache.Bytes); // same size reply: replacing must not have doubled the budget
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, int millis = 2000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < millis)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return condition();
    }

    /// <summary>Eviction prefers entries that are already dead, and only then the oldest of a sample.</summary>
    /// <remarks>
    /// The sample is being walked anyway, so a dead entry is taken on sight: it costs nothing to release
    /// and, unlike a live one, nobody wanted it. Anything else would evict something useful while something
    /// useless sat next to it.
    /// </remarks>
    [Fact]
    public void EvictionTakesDeadEntriesBeforeLiveOnes()
    {
        // a sample at least as large as the table means the whole table is examined, so this asserts the
        // preference rather than the luck of which window was sampled
        using var cache = new RespClientCache(new CacheOptions { MaxEntries = 10, EvictionSampleSize = 64 });

        Fill(cache, 10);
        Assert.Equal(10, cache.Count);

        // key3 is invalidated but still resident - it is key0 that is OLDEST, so an age-only policy would
        // take that one and leave the dead entry sitting there
        cache.OnInvalidate(Utf8("key3"));

        var extra = Get("pushes-us-over");
        Assert.True(cache.TryBeginFill(ref extra, 0, out var fill));
        Assert.True(Complete(cache, fill, "$1\r\nx\r\n"));

        Assert.Equal(10, cache.Count);
        Assert.Equal(1, cache.Evicted);

        using var dead = Get("key3");
        Assert.False(cache.TryGet(dead.AsLookupKey(), 0, out _), "the dead entry should have been the one to go");

        using var oldest = Get("key0");
        Assert.True(cache.TryGet(oldest.AsLookupKey(), 0, out var alive), "the oldest LIVE entry should have survived");
        alive.Release();
    }

    /// <summary>Budgets must be positive; null is how you say "no limit".</summary>
    [Fact]
    public void NonPositiveBudgetsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CacheOptions { MaxBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CacheOptions { MaxEntries = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CacheOptions { EvictionSampleSize = 0 });

        // and unbounded is the default, because that is what a cache without a budget actually is
        Assert.Null(CacheOptions.Default.MaxBytes);
        Assert.Null(CacheOptions.Default.MaxEntries);
    }

    /// <summary>
    /// A write of ours evicts what we had cached for that key, without waiting for the server to say so.
    /// </summary>
    /// <remarks>
    /// The window this closes is real and measured: a write's own invalidation arrives after its reply, and
    /// after the replies of anything pipelined behind it, so the server can never tell us in time. Without
    /// this the read below is answered from cache with the value we just replaced - which is not staleness
    /// a caller can shrug at, it is a wrong answer.
    /// </remarks>
    [Fact]
    public async Task OurOwnWriteEvictsWhatWeCached()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$2\r\nv1\r\n");
        var context = Via(executor, cache);

        var read = Get("abc");
        Assert.Equal("$2|v1|", await context.SendAsync(ref read, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default));
        Assert.Equal(1, executor.Sent);

        // cached: a second read does not reach the executor
        var again = Get("abc");
        Assert.Equal("$2|v1|", await context.SendAsync(ref again, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default));
        Assert.Equal(1, executor.Sent);

        // now WE write it - no server invalidation involved anywhere in this test
        var write = Ctx.Render($"{RedisCommand.SET}{(RedisKey)"abc"}{(RedisValue)"v2"}");
        await context.SendAsync(ref write, CommandFlags.CommandRetryWriteLastWins, TextHandler.Instance, default);

        // ...and the next read must go and ask, rather than hand back what we just replaced
        var after = Get("abc");
        Assert.Equal("$2|v1|", await context.SendAsync(ref after, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default));
        Assert.Equal(3, executor.Sent); // read, write, re-read
    }

    /// <summary>A read of ours does not invalidate anything.</summary>
    /// <remarks>
    /// The other half: if the mutation test were simply "anything that is not cacheable invalidates", a
    /// fire-and-forget read or a NoClientCache read would evict the very entry it declined to use.
    /// </remarks>
    [Theory]
    [InlineData(CommandFlags.CommandRetryReadOnly)]
    [InlineData(CommandFlags.CommandRetryReadOnly | CommandFlags.FireAndForget)]
    [InlineData(CommandFlags.CommandRetryReadOnly | CommandFlags.NoClientCache)]
    public async Task ReadsDoNotInvalidate(CommandFlags readFlags)
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$2\r\nv1\r\n");
        var context = Via(executor, cache);

        var read = Get("abc");
        await context.SendAsync(ref read, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);
        Assert.Equal(1, cache.Count);

        var other = Get("abc");
        await context.SendAsync(ref other, readFlags, TextHandler.Instance, default);

        // still cached, and still servable
        using var probe = Get("abc");
        Assert.True(cache.TryGet(probe.AsLookupKey(), 0, out var hit));
        hit.Release();
    }

    /// <summary>
    /// An undeclared retry category is treated as a write.
    /// </summary>
    /// <remarks>
    /// The same judgement the caching side makes from the other direction. Undeclared cannot mean safe: for
    /// storing it means "do not", and for invalidating it means "assume it wrote". An ad-hoc command
    /// through <c>ExecuteAsync</c> with no category is exactly this case.
    /// </remarks>
    [Fact]
    public async Task AnUndeclaredCategoryIsAssumedToWrite()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$2\r\nv1\r\n");
        var context = Via(executor, cache);

        var read = Get("abc");
        await context.SendAsync(ref read, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);
        Assert.Equal(1, cache.Count);

        var unknown = Ctx.Render($"{RedisCommand.SET}{(RedisKey)"abc"}{(RedisValue)"v2"}");
        await context.SendAsync(ref unknown, CommandFlags.None, TextHandler.Instance, default);

        using var probe = Get("abc");
        Assert.False(cache.TryGet(probe.AsLookupKey(), 0, out _), "an undeclared command should be assumed to write");
    }

    /// <summary>
    /// A write whose key marks overflowed invalidates its own keys - and <b>not</b> the rest of the cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A frame can only mark keys up to argument 62, so a large <c>MSET</c> or <c>DEL</c> cannot say which
    /// of its arguments were keys. Reporting a subset is forbidden, but the arguments are a <i>superset</i>
    /// of the keys, so stamping all of them is correct and stays inside the command.
    /// </para>
    /// <para>
    /// The first version of this flushed the whole cache, which would have made a bulk write destroy an
    /// unrelated hot cache every time - the exact question that prompted looking again.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AWriteWithUnknowableKeysInvalidatesOnlyItsOwn()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$2\r\nv1\r\n");
        var context = Via(executor, cache);

        foreach (var key in new[] { "a", "b", "c" })
        {
            var read = Get(key);
            await context.SendAsync(ref read, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);
        }

        Assert.Equal(3, cache.Count);

        // more keys than the frame can mark individually: KeyCount goes negative, meaning "not enumerable"
        var many = Ctx.Compose($"{RedisCommand.MSET}");
        for (var i = 0; i < 80; i++) many.Append($"{(RedisKey)("k" + i)}{(RedisValue)"v"}");
        var frame = Ctx.Render(ref many);
        Assert.True(frame.KeyCount < 0, $"expected unknowable keys, got KeyCount={frame.KeyCount}");

        await context.SendAsync(ref frame, CommandFlags.CommandRetryWriteLastWins, TextHandler.Instance, default);

        // the unrelated entries survive: this write never mentioned them
        foreach (var key in new[] { "a", "b", "c" })
        {
            using var probe = Get(key);
            Assert.True(cache.TryGet(probe.AsLookupKey(), 0, out var alive), $"'{key}' should not have been touched");
            alive.Release();
        }
    }

    /// <summary>...and the keys such a write DID name are invalidated, marks or no marks.</summary>
    /// <remarks>
    /// The other half of the superset argument: stamping the arguments is only acceptable because it is a
    /// superset. If it missed a key past the bitmap, the write would go unannounced locally and the entry
    /// would be served until the server's echo caught up - which is the window this whole mechanism exists
    /// to close.
    /// </remarks>
    [Fact]
    public async Task AWriteWithUnknowableKeysStillInvalidatesTheKeysItNamed()
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$2\r\nv1\r\n");
        var context = Via(executor, cache);

        // k70 sits well past the bitmap's last markable argument
        var read = Get("k70");
        await context.SendAsync(ref read, CommandFlags.CommandRetryReadOnly, TextHandler.Instance, default);
        Assert.Equal(1, cache.Count);

        var many = Ctx.Compose($"{RedisCommand.MSET}");
        for (var i = 0; i < 80; i++) many.Append($"{(RedisKey)("k" + i)}{(RedisValue)"v"}");
        var frame = Ctx.Render(ref many);
        Assert.True(frame.KeyCount < 0);

        await context.SendAsync(ref frame, CommandFlags.CommandRetryWriteLastWins, TextHandler.Instance, default);

        using var probe = Get("k70");
        Assert.False(cache.TryGet(probe.AsLookupKey(), 0, out _), "a key past the bitmap must still be invalidated");
    }

}
