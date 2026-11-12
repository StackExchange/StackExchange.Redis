using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Per-key removal notification: a <see cref="CachePolicy.RemovalHandler"/> - per caller via
/// <see cref="RespDatabaseContext.WithCachePolicy"/>, or connection-wide on the default policy - is
/// told when a key read under it stops being servable from the client-side cache.
/// </summary>
/// <remarks>
/// <para>
/// The contract under test: enrollment is armed by a serve (hit or fill) and consumed by the first
/// removal at or after it - one notification per enrollment, re-armed by reading again. It fires at
/// invalidation time (the entry may still be physically resident), from the thread pool - so every test
/// collects events through <see cref="Recorder"/> and awaits them rather than asserting inline.
/// </para>
/// <para>
/// Deaths that cannot be announced are not enrolled at all (a fill whose tracking prefix was dropped),
/// and a value handed out already-dead - a fill that raced an invalidation, a stale-grace serve - fires
/// immediately rather than waiting for a removal that already happened.
/// </para>
/// </remarks>
public class RespCacheRemovalTests
{
    private const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;

    private static readonly RespContext Renderer = new();

    private static (RespDatabaseContext Context, RespClientCache Cache, FakeExecutor Executor, Recorder Recorder) Create(
        CacheOptions? options = null,
        params string[] replies)
    {
        options ??= new CacheOptions { Admission = CacheAdmission.OnFirstMiss };
        var cache = new RespClientCache(options);
        var executor = new FakeExecutor(replies);
        var recorder = new Recorder();

        // the caller's read policy is the cache's own, plus the observer: derive, don't rebuild
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache))
            .WithCachePolicy(options.DefaultPolicy with { RemovalHandler = recorder.Handler });
        return (context, cache, executor, recorder);
    }

    [Fact]
    public async Task AForeignInvalidationFiresWithTheKeyAndReason_WhileTheEntryIsStillResident()
    {
        var (context, cache, _, recorder) = Create(replies: "$1\r\na\r\n");
        using (cache)
        {
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

            cache.OnInvalidate("k"u8);

            var (key, reason) = await recorder.NextAsync();
            Assert.Equal("k", key);
            Assert.Equal(CacheKeyRemovalReason.Invalidated, reason);

            // fired at INVALIDATION time: the corpse is still in the dictionary, nothing swept it
            Assert.Equal(1, cache.Count);
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task OurOwnWriteSaysLocalWrite()
    {
        var (context, cache, _, recorder) = Create(null, "$1\r\na\r\n", "+OK\r\n");
        using (cache)
        {
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            _ = await context.SendAsync<string>($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"b"}", CommandFlags.None);

            var (key, reason) = await recorder.NextAsync();
            Assert.Equal("k", key);
            Assert.Equal(CacheKeyRemovalReason.LocalWrite, reason);
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task OneEnrollmentFiresOnce_AndReadingAgainReArms()
    {
        var (context, cache, _, recorder) = Create(replies: "$1\r\na\r\n");
        using (cache)
        {
            // several reads, ONE enrollment: arming is idempotent per handler instance
            for (var i = 0; i < 3; i++)
            {
                _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            }

            cache.OnInvalidate("k"u8);
            Assert.Equal(CacheKeyRemovalReason.Invalidated, (await recorder.NextAsync()).Reason);

            // a second invalidation of the dead key says nothing: the first one consumed the enrollment
            cache.OnInvalidate("k"u8);
            await recorder.AssertNoFurtherAsync();

            // reading again re-arms, and the next removal is announced again
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            cache.OnInvalidate("k"u8);
            Assert.Equal(CacheKeyRemovalReason.Invalidated, (await recorder.NextAsync()).Reason);
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task AFillThatRacedAnInvalidationFiresImmediately()
    {
        // the invalidation lands between the fill's generation capture and its completion: the store is
        // refused, and the consumer - who was just handed a value of unprovable currency - is told now,
        // because the removal it would otherwise wait for has already happened
        var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss });
        var executor = new InvalidatingExecutor(cache, "k", "$1\r\na\r\n");
        var recorder = new Recorder();
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache))
            .WithCachePolicy(new CachePolicy { RemovalHandler = recorder.Handler });
        using (cache)
        {
            var value = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            Assert.Equal("a", value); // served to the caller regardless

            var (key, reason) = await recorder.NextAsync();
            Assert.Equal("k", key);
            Assert.Equal(CacheKeyRemovalReason.Invalidated, reason);
            Assert.Equal(0, cache.Count); // and indeed, nothing was stored
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task AReplacingRefreshSaysReplaced()
    {
        // refresh-ahead: a read past RefreshAfter re-fetches in the background, and the completed
        // refresh REPLACES the live entry - the one removal that happens with no invalidation at all
        var options = new CacheOptions
        {
            Admission = CacheAdmission.OnFirstMiss,
            DefaultPolicy = new CachePolicy { RefreshAfter = TimeSpan.FromMilliseconds(1) },
        };
        var (context, cache, _, recorder) = Create(options, "$1\r\na\r\n", "$1\r\nb\r\n");
        using (cache)
        {
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            await Task.Delay(50); // comfortably past RefreshAfter, well inside TimeToLive

            // this hit is served the old value AND claims the refresh that will replace it
            Assert.Equal("a", await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable));

            var (key, reason) = await recorder.NextAsync();
            Assert.Equal("k", key);
            Assert.Equal(CacheKeyRemovalReason.Replaced, reason);
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task EvictionUnderBudgetSaysEvicted()
    {
        var options = new CacheOptions { Admission = CacheAdmission.OnFirstMiss, MaxEntries = 1 };
        var (context, cache, _, recorder) = Create(options, "$1\r\na\r\n");
        using (cache)
        {
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k1"}", Readable);

            // the second entry pushes the first out: nothing wrong with it, the budget just ran out -
            // and the server may still track it, so the local death must be announced, not inferred
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k2"}", Readable);

            var (key, reason) = await recorder.NextAsync();
            Assert.Equal("k1", key);
            Assert.Equal(CacheKeyRemovalReason.Evicted, reason);
        }
    }

    [Fact]
    public async Task AnEntryThatAgesOutSaysExpiredWhenReclaimed()
    {
        var options = new CacheOptions
        {
            Admission = CacheAdmission.OnFirstMiss,
            DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromMilliseconds(1) },
        };
        var (context, cache, _, recorder) = Create(options, "$1\r\na\r\n");
        using (cache)
        {
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            await Task.Delay(50); // comfortably past the lifetime

            Assert.Equal(1, cache.Sweep()); // lazy expiry: the sweep is the reclamation moment
            var (key, reason) = await recorder.NextAsync();
            Assert.Equal("k", key);
            Assert.Equal(CacheKeyRemovalReason.Expired, reason);
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task AFlushSaysFlushedPromptly()
    {
        var (context, cache, _, recorder) = Create(replies: "$1\r\na\r\n");
        using (cache)
        {
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

            cache.OnFlush(); // reconnect, FLUSHALL, tracking torn down - no sweep involved

            var (key, reason) = await recorder.NextAsync();
            Assert.Equal("k", key);
            Assert.Equal(CacheKeyRemovalReason.Flushed, reason);
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task TwoConsumersOnOneKeyBothHear()
    {
        var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss });
        var executor = new FakeExecutor("$1\r\na\r\n");
        var shared = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));
        var first = new Recorder();
        var second = new Recorder();
        using (cache)
        {
            _ = await shared.WithCachePolicy(new CachePolicy { RemovalHandler = first.Handler }).SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            _ = await shared.WithCachePolicy(new CachePolicy { RemovalHandler = second.Handler }).SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

            cache.OnInvalidate("k"u8);

            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await first.NextAsync());
            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await second.NextAsync());
        }
    }

    [Fact]
    public async Task AThrowingConsumerDoesNotSilenceAnother()
    {
        var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss });
        var executor = new FakeExecutor("$1\r\na\r\n");
        var shared = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));
        CacheKeyRemovedHandler throwing = (_, _) => throw new InvalidOperationException("deliberately rude");
        var recorder = new Recorder();
        using (cache)
        {
            // the throwing handler enrolls FIRST, so a propagated exception would take the second with it
            _ = await shared.WithCachePolicy(new CachePolicy { RemovalHandler = throwing }).SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            _ = await shared.WithCachePolicy(new CachePolicy { RemovalHandler = recorder.Handler }).SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

            cache.OnInvalidate("k"u8);

            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await recorder.NextAsync());
        }
    }

    [Fact]
    public async Task AHitEnrollsAnEntryThatWasFilledThroughAPlainContext()
    {
        var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss });
        var executor = new FakeExecutor("$1\r\na\r\n");
        var plain = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));
        var recorder = new Recorder();
        using (cache)
        {
            _ = await plain.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable); // fill, no observer
            _ = await plain.WithCachePolicy(new CachePolicy { RemovalHandler = recorder.Handler }).SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable); // hit enrolls

            cache.OnInvalidate("k"u8);

            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await recorder.NextAsync());
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task AHandlerOnTheDefaultPolicyIsConnectionWide()
    {
        // no per-context policy anywhere: the observer rides the cache's own DefaultPolicy, so EVERY
        // read enrolls it - the firehose a backplane wants, with the same read-armed one-shot contract
        var recorder = new Recorder();
        var cache = new RespClientCache(new CacheOptions
        {
            Admission = CacheAdmission.OnFirstMiss,
            DefaultPolicy = new CachePolicy { RemovalHandler = recorder.Handler },
        });
        var executor = new FakeExecutor("$1\r\na\r\n");
        var plain = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));
        using (cache)
        {
            _ = await plain.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

            cache.OnInvalidate("k"u8);

            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await recorder.NextAsync());
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task TheKeyArrivesExactlyAsStored_PrefixesIncluded()
    {
        var (context, cache, _, recorder) = Create(replies: "$1\r\na\r\n");
        using (cache)
        {
            _ = await context.AppendKeyPrefix("p:").SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

            cache.OnInvalidate("p:k"u8); // the server names the rendered key, and so does the notification

            var (key, reason) = await recorder.NextAsync();
            Assert.Equal("p:k", key);
            Assert.Equal(CacheKeyRemovalReason.Invalidated, reason);
        }
    }

    [Fact]
    public async Task AStaleGraceServeIsAnnouncedImmediately()
    {
        var options = new CacheOptions
        {
            Admission = CacheAdmission.OnFirstMiss,
            DefaultPolicy = new CachePolicy { InvalidationGracePeriod = TimeSpan.FromSeconds(30) },
        };
        var (context, cache, _, recorder) = Create(options, "$1\r\na\r\n");
        using (cache)
        {
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

            cache.OnInvalidate("k"u8);
            Assert.Equal(CacheKeyRemovalReason.Invalidated, (await recorder.NextAsync()).Reason);

            // within the grace window the dead value is still served - and the serve says so at once,
            // rather than handing a non-current value over in silence
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            Assert.Equal(CacheKeyRemovalReason.Invalidated, (await recorder.NextAsync()).Reason);
        }
    }

    [Fact]
    public void SlabCompactionIsNotARemoval_AndTheSurvivingEnrollmentStillFires()
    {
        // the slab-churn setup from AReadEntryIsCarriedForwardWhenItsSlabIsEvicted: a hot entry is
        // carried forward (CopyForward) when its slab is evicted. Enrollment lives on the key's NODE,
        // so the move must say nothing - and the enrollment must still be armed afterwards
        const long budget = 4L * 1024 * 1024;
        var reply = "$8000\r\n" + new string('x', 8000) + "\r\n";
        var recorder = new Recorder();
        var observing = new CachePolicy { RemovalHandler = recorder.Handler };
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss, MaxBytes = budget });

        Fill(cache, "hot", reply, observing);
        for (var i = 0; i < 10_000 && cache.Promoted == 0; i++)
        {
            Fill(cache, "filler" + i, reply);
            if (i % 4 == 0)
            {
                using var frame = Renderer.Render($"{RedisCommand.GET}{(RedisKey)"hot"}");
                Assert.True(cache.TryGet(frame.AsLookupKey(Readable), 0, observing, out var payload, out _, out _));
                payload.Release();
            }
        }

        Assert.True(cache.Promoted > 0, "the hot entry was never carried forward");
        recorder.AssertNothingYet(); // the move is not a removal

        cache.OnInvalidate("hot"u8);
        var (key, reason) = recorder.Next();
        Assert.Equal("hot", key);
        Assert.Equal(CacheKeyRemovalReason.Invalidated, reason);
    }

    /// <summary>Render, fill, and complete - optionally under an observing policy, as the funnel would.</summary>
    private static void Fill(RespClientCache cache, string key, string response, CachePolicy? policy = null)
    {
        var frame = Renderer.Render($"{RedisCommand.GET}{(RedisKey)key}");
        if (!cache.TryBeginFill(ref frame, 0, Readable, out var fill))
        {
            frame.Dispose();
            return;
        }

        Complete(cache, fill, response, policy);
    }

    private static void Complete(RespClientCache cache, in RespClientCache.RespFill fill, string response, CachePolicy? policy = null)
    {
        var payload = RespPayload.Create(Encoding.UTF8.GetBytes(response));
        try
        {
            cache.TryComplete(fill, payload, policy);
        }
        finally
        {
            payload.Release();
        }
    }

    /// <summary>Invalidates the key after every send: a write landing while the fill is in flight.</summary>
    private sealed class InvalidatingExecutor(RespClientCache cache, string key, params string[] replies) : FakeExecutor(replies)
    {
        protected override void OnSent(in RespRequest request) => cache.OnInvalidate(Encoding.UTF8.GetBytes(key));
    }

    /// <summary>
    /// Collects notifications - which arrive from the thread pool - so tests can await them.
    /// </summary>
    private sealed class Recorder
    {
        private readonly ConcurrentQueue<(string Key, CacheKeyRemovalReason Reason)> _events = new();
        private readonly SemaphoreSlim _signal = new(0);

        public Recorder()
        {
            // ONE delegate instance for the recorder's lifetime: enrollment dedupes on its identity
            Handler = (key, reason) =>
            {
                _events.Enqueue((Encoding.UTF8.GetString(key.ToArray()), reason));
                _signal.Release();
            };
        }

        public CacheKeyRemovedHandler Handler { get; }

        public async Task<(string Key, CacheKeyRemovalReason Reason)> NextAsync()
        {
            Assert.True(await _signal.WaitAsync(TimeSpan.FromSeconds(10)), "timed out waiting for a removal notification");
            Assert.True(_events.TryDequeue(out var fired));
            return fired;
        }

        public (string Key, CacheKeyRemovalReason Reason) Next()
        {
            Assert.True(_signal.Wait(TimeSpan.FromSeconds(10)), "timed out waiting for a removal notification");
            Assert.True(_events.TryDequeue(out var fired));
            return fired;
        }

        /// <summary>Settle the pool briefly and assert no stray notification arrived.</summary>
        public async Task AssertNoFurtherAsync()
        {
            if (await _signal.WaitAsync(TimeSpan.FromMilliseconds(250)))
            {
                _events.TryDequeue(out var fired);
                Assert.Fail($"unexpected notification: {fired}");
            }
        }

        /// <summary>As <see cref="AssertNoFurtherAsync"/>, for a synchronous test.</summary>
        public void AssertNothingYet()
        {
            if (_signal.Wait(TimeSpan.FromMilliseconds(250)))
            {
                _events.TryDequeue(out var fired);
                Assert.Fail($"unexpected notification: {fired}");
            }
        }
    }
}
