using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Caching;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Per-call and per-policy control over HOW a command participates in client-side caching, between the
/// all-or-nothing of <see cref="CommandFlags.CacheOptIn"/>/<see cref="CommandFlags.CacheOptOut"/>:
/// <see cref="CommandFlags.CacheNoLocalServe"/> (always a round trip, everything else as normal),
/// <see cref="CommandFlags.CacheNoLocalStore"/> (serve if present, never keep, never enroll), and
/// <see cref="CacheStorage.External"/> on the policy (never keep, but track and enroll - the external
/// cache layer's mode).
/// </summary>
/// <remarks>
/// The flag pair and the policy divide one question - "nothing is stored, so is anything watched?" - on
/// intent. <see cref="CommandFlags.CacheNoLocalStore"/> is the bulk read that wants to leave no trace:
/// nothing kept, nothing watched. <see cref="CacheStorage.External"/> is the layer that keeps the values
/// itself and reads <i>in order to be told</i>: nothing kept, everything watched. Overloading the flag
/// with the policy's semantics would poison both.
/// </remarks>
public class RespCacheParticipationTests
{
    private const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;

    private static (RespDatabaseContext Context, RespClientCache Cache, FakeExecutor Executor) Create(
        CacheOptions? options = null,
        params string[] replies)
    {
        options ??= new CacheOptions { Admission = CacheAdmission.OnFirstMiss };
        var cache = new RespClientCache(options);
        var executor = new FakeExecutor(replies);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));
        return (context, cache, executor);
    }

    [Fact]
    public async Task NoLocalServeForcesARoundTripPastAWarmEntry_AndStillEnrolls()
    {
        var (context, cache, executor) = Create(null, "$1\r\na\r\n", "$1\r\nb\r\n");
        var recorder = new Recorder();
        var observing = context.WithCachePolicy(new CachePolicy { RemovalHandler = recorder.Handler });
        using (cache)
        {
            Assert.Equal("a", await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable));
            Assert.Equal(1, executor.Sends);

            // the warm entry is ignored on the way in: this caller gets the server's answer
            Assert.Equal(
                "b",
                await observing.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable | CommandFlags.CacheNoLocalServe));
            Assert.Equal(2, executor.Sends);

            // ...and everything else about the round trip stood: the removal handler armed (the store
            // itself lost to the resident entry, which is first-fill-wins as for any racing fill)
            cache.OnInvalidate("k"u8);
            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await recorder.NextAsync());
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task NoLocalServeOnAColdCacheStillAdmits()
    {
        var (context, cache, executor) = Create(replies: "$1\r\na\r\n");
        using (cache)
        {
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable | CommandFlags.CacheNoLocalServe);
            Assert.Equal(1, cache.Count); // eligible for admission, exactly as any round trip

            // the next ordinary read is served from what the forced round trip stored
            Assert.Equal("a", await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable));
            Assert.Equal(1, executor.Sends);
        }
    }

    [Fact]
    public async Task NoLocalStoreOnAColdCacheLeavesItEmpty_AndEnrollsNothing()
    {
        var (context, cache, executor) = Create(null, "$1\r\na\r\n", "$1\r\na\r\n");
        var recorder = new Recorder();
        var observing = context.WithCachePolicy(new CachePolicy { RemovalHandler = recorder.Handler });
        using (cache)
        {
            _ = await observing.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable | CommandFlags.CacheNoLocalStore);
            Assert.Equal(0, cache.Count);

            // nothing kept means nothing watched: there was no fill for the handler to arm on
            cache.OnInvalidate("k"u8);
            await recorder.AssertNoFurtherAsync();

            // and nothing was stored for the next read to find
            _ = await observing.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable | CommandFlags.CacheNoLocalStore);
            Assert.Equal(2, executor.Sends);
        }
    }

    [Fact]
    public async Task NoLocalStoreIsServedFromAWarmEntry_WhichReArmsAsUsual()
    {
        var (context, cache, executor) = Create(replies: "$1\r\na\r\n");
        var recorder = new Recorder();
        var observing = context.WithCachePolicy(new CachePolicy { RemovalHandler = recorder.Handler });
        using (cache)
        {
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

            // serving is not storing: the flag forbids keeping this reply, not reading someone else's
            Assert.Equal(
                "a",
                await observing.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable | CommandFlags.CacheNoLocalStore));
            Assert.Equal(1, executor.Sends);

            // and the SERVE armed the handler, exactly as an unflagged hit would
            cache.OnInvalidate("k"u8);
            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await recorder.NextAsync());
        }
    }

    [Fact]
    public async Task BothFlagsTogetherAreFullNonParticipation()
    {
        var (context, cache, executor) = Create(null, "$1\r\na\r\n", "$1\r\na\r\n");
        var recorder = new Recorder();
        var observing = context.WithCachePolicy(new CachePolicy { RemovalHandler = recorder.Handler });
        const CommandFlags both = CommandFlags.CacheNoLocalServe | CommandFlags.CacheNoLocalStore;
        using (cache)
        {
            _ = await observing.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable | both);
            _ = await observing.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable | both);

            Assert.Equal(2, executor.Sends);
            Assert.Equal(0, cache.Count);

            cache.OnInvalidate("k"u8);
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task TheFlagsAreInertOnANonParticipatingCommand()
    {
        var (context, cache, executor) = Create(null, "$1\r\na\r\n", "$1\r\na\r\n");
        using (cache)
        {
            // CacheOptOut already took the command out of caching; the narrower flags are redundant,
            // not rejected - the reserved-pair check is only about OptIn|OptOut
            const CommandFlags flags = Readable
                                       | CommandFlags.CacheOptOut
                                       | CommandFlags.CacheNoLocalServe
                                       | CommandFlags.CacheNoLocalStore;
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", flags);
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", flags);
            Assert.Equal(2, executor.Sends);
            Assert.Equal(0, cache.Count);
        }
    }

    [Fact]
    public async Task ExternalStorageTracksAndEnrollsWithoutStoring()
    {
        // DEFAULT admission - the doorkeeper would refuse a first miss - to prove the external policy
        // bypasses it the way CacheOptIn does: a read whose purpose is enrollment must not be dropped
        // by a rule about what is worth keeping
        var (context, cache, executor) = Create(new CacheOptions(), "$1\r\na\r\n", "$1\r\na\r\n");
        var recorder = new Recorder();
        var external = context.WithCachePolicy(new CachePolicy
        {
            Storage = CacheStorage.External,
            RemovalHandler = recorder.Handler,
        });
        using (cache)
        {
            Assert.Equal("a", await external.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable));
            Assert.Equal(0, cache.Count); // the external layer keeps the value, not us
            Assert.Equal(1, cache.RefusedExternal);

            // ...but the key IS tracked, and the handler heard about it from the very first read
            cache.OnInvalidate("k"u8);
            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await recorder.NextAsync());
            await recorder.AssertNoFurtherAsync();

            // reading again re-arms, exactly as the enrollment contract says
            _ = await external.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            Assert.Equal(2, executor.Sends); // nothing was stored, so every read is a round trip
            cache.OnInvalidate("k"u8);
            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await recorder.NextAsync());
            Assert.Equal(0, cache.Count);
        }
    }

    [Fact]
    public async Task ExternalStorageIsServedFromEntriesOthersStored()
    {
        var (context, cache, executor) = Create(replies: "$1\r\na\r\n");
        var recorder = new Recorder();
        var external = context.WithCachePolicy(new CachePolicy
        {
            Storage = CacheStorage.External,
            RemovalHandler = recorder.Handler,
        });
        using (cache)
        {
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

            // serving is not storing: the locally cached value is as current as any round trip's,
            // and the serve arms the handler exactly as a fill would
            Assert.Equal("a", await external.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable));
            Assert.Equal(1, executor.Sends);

            cache.OnInvalidate("k"u8);
            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await recorder.NextAsync());
        }
    }

    [Fact]
    public async Task ExternalStorageHearsOurOwnWrites()
    {
        var (context, cache, _) = Create(null, "$1\r\na\r\n", "+OK\r\n");
        var recorder = new Recorder();
        var external = context.WithCachePolicy(new CachePolicy
        {
            Storage = CacheStorage.External,
            RemovalHandler = recorder.Handler,
        });
        using (cache)
        {
            _ = await external.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

            // the one removal whose replacement value the external layer already has: its own process
            // wrote it - worth distinguishing from a foreign invalidation, so the reason says so
            _ = await context.SendAsync<string>($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"b"}", CommandFlags.None);

            Assert.Equal(("k", CacheKeyRemovalReason.LocalWrite), await recorder.NextAsync());
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task ExternalStorageOnTheDefaultPolicyIsConnectionWide()
    {
        var recorder = new Recorder();
        var options = new CacheOptions
        {
            DefaultPolicy = new CachePolicy { Storage = CacheStorage.External, RemovalHandler = recorder.Handler },
        };
        var (context, cache, _) = Create(options, "$1\r\na\r\n");
        using (cache)
        {
            // a PLAIN read on a plain context: the connection-wide policy does all the work
            _ = await context.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            Assert.Equal(0, cache.Count);

            cache.OnInvalidate("k"u8);
            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await recorder.NextAsync());
            await recorder.AssertNoFurtherAsync();
        }
    }

    [Fact]
    public async Task ExternalStorageWorksUnderAnOptInTrackingMode()
    {
        // under TrackingOptIn nothing is tracked unless a read says CLIENT CACHING YES; the external
        // policy implies CacheOptIn, so its reads say it - without that, "you will be told" would be
        // a promise the server never heard about
        var recorder = new Recorder();
        var options = new CacheOptions { TrackingMode = CacheTrackingMode.TrackingOptIn };
        var (context, cache, _) = Create(options, "$1\r\na\r\n");
        var external = context.WithCachePolicy(new CachePolicy
        {
            Storage = CacheStorage.External,
            RemovalHandler = recorder.Handler,
        });
        using (cache)
        {
            _ = await external.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);
            Assert.Equal(0, cache.Count);

            cache.OnInvalidate("k"u8);
            Assert.Equal(("k", CacheKeyRemovalReason.Invalidated), await recorder.NextAsync());
        }
    }

    [Fact]
    public async Task NoLocalStoreWinsOverAnExternalPolicyForThatOneCommand()
    {
        var (context, cache, _) = Create(replies: "$1\r\na\r\n");
        var recorder = new Recorder();
        var external = context.WithCachePolicy(new CachePolicy
        {
            Storage = CacheStorage.External,
            RemovalHandler = recorder.Handler,
        });
        using (cache)
        {
            // the per-call flag is the narrower statement: this one read leaves no trace, policy or no
            _ = await external.SendAsync<string>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable | CommandFlags.CacheNoLocalStore);
            Assert.Equal(0, cache.Count);

            cache.OnInvalidate("k"u8);
            await recorder.AssertNoFurtherAsync();
        }
    }

    /// <summary>Collects notifications - which arrive from the thread pool - so tests can await them.</summary>
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

        /// <summary>Settle the pool briefly and assert no stray notification arrived.</summary>
        public async Task AssertNoFurtherAsync()
        {
            if (await _signal.WaitAsync(TimeSpan.FromMilliseconds(250)))
            {
                _events.TryDequeue(out var fired);
                Assert.Fail($"unexpected notification: {fired}");
            }
        }
    }
}
