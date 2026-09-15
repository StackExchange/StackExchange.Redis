using System;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Client-side caching through the <b>real</b> multiplexer: a cache owned by the connection, fed by
/// invalidation pushes that arrive on the same socket as everything else.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RespTrackingTests"/> proves the same loop against a hand-rolled socket, which was the right
/// way to learn the wire format but says nothing about whether <see cref="PhysicalConnection"/> routes an
/// invalidation to a cache rather than dropping it as an unrecognized push - or worse, handing it to
/// whichever command happened to be at the front of the queue.
/// </para>
/// <para>
/// Tracking is switched on by hand here. Negotiating it as part of the handshake is a separate step; until
/// then this is the shape a caller would have to use, and it exercises exactly the same routing.
/// </para>
/// </remarks>
// CLIENT TRACKING here is not scoped to this class: a FLUSHDB sends an UNFILTERABLE flush push to every
// tracking client on the server, including the ones RespTrackingTests is counting invalidations on. That is
// the design working as documented (6.13) and a test interfering with another test, so these do not overlap.
[Collection(NonParallelCollection.Name)]
public class RespCacheInvalidationTests(ITestOutputHelper output) : TestBase(output)
{
    private static async Task<bool> WaitFor(Func<Task<bool>> condition, int millis = 3000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < millis)
        {
            if (await condition()) return true;
            await Task.Delay(25);
        }

        return await condition();
    }

    /// <summary>
    /// Read a key until the reply actually lands in the cache.
    /// </summary>
    /// <remarks>
    /// <b>Not flakiness-papering: this is the cache being right.</b> Under <c>BCAST</c> the server announces
    /// a matching key to every tracking client the moment <i>anyone</i> writes it - including the setup
    /// write this test just did on another connection. If that push overtakes the reply we are filling
    /// from, the fill is refused as raced, because storing it would cache a value the server has already
    /// said is wrong. So a test that primes an entry immediately after writing it has to be prepared to ask
    /// twice, and one that is not simply fails - which is how this was found.
    /// </remarks>
    private static async Task PrimeAsync(IDatabase db, RespClientCache cache, RedisKey key, string expected)
    {
        var target = cache.Count + 1;
        Assert.True(
            await WaitFor(async () =>
            {
                Assert.Equal(expected, (string?)await db.Strings.Get(key));
                return cache.Count >= target;
            }),
            $"'{key}' never cached: stored={cache.Stored} raced={cache.RefusedRaced} " +
            $"flags={cache.RefusedByFlags} nokeys={cache.RefusedNoKeys} err={cache.RefusedError}");
    }

    /// <summary>
    /// A cache-enabled multiplexer with <c>CLIENT TRACKING</c> on, scoped to one prefix.
    /// </summary>
    /// <remarks>
    /// The prefix is not decoration: under <c>BCAST</c> with no prefix this connection is told about every
    /// key the rest of the suite touches, and any assertion about a particular key is then competing with
    /// that traffic.
    /// <para>
    /// The <b>same</b> prefix goes on the policy and on the wire, which is the point of
    /// <see cref="CacheOptions.Prefixes"/>: the set the cache will admit and the set the server agreed to
    /// announce have to be one set, or entries fall in the gap and stay there. Nothing here sends
    /// <c>CLIENT TRACKING</c> - the handshake does, from these same options, which is why configuring them
    /// is all this needs to do. It used to issue the command by hand; once the library started doing it too,
    /// the server rejected the duplicate as an overlapping prefix, which is a fair complaint.
    /// </para>
    /// </remarks>
    private async Task<(ConnectionMultiplexer Muxer, RespClientCache Cache)> TrackedAsync(
        string prefix,
        CacheOptions? cacheOptions = null,
        int? database = null)
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { { TestConfig.Current.PrimaryServer, TestConfig.Current.PrimaryPort } },
            Protocol = RedisProtocol.Resp3,
            ClientCache = cacheOptions ?? new CacheOptions { Prefixes = [prefix] },
            DefaultDatabase = database,
            AllowAdmin = true,
        };

        ConnectionMultiplexer muxer;
        try
        {
            muxer = await ConnectionMultiplexer.ConnectAsync(options, Writer);
        }
        catch (Exception ex)
        {
            Assert.Skip("Unable to connect to server: " + ex.Message);
            throw;
        }

        var cache = muxer.ClientCache;
        Assert.NotNull(cache);

        return (muxer, cache);
    }

    [Fact]
    public async Task AWriteElsewhereInvalidatesWhatTheMultiplexerCached()
    {
        var me = Me();
        var (muxer, cache) = await TrackedAsync(me);
        using var _ = muxer;

        // a second connection, with no cache and no tracking: the "somebody else" whose writes we must hear about
        using var other = await ConnectionMultiplexer.ConnectAsync(TestConfig.Current.PrimaryServerAndPort, Writer);
        var writer = other.GetDatabase();

        RedisKey tracked = me + ":tracked", untracked = "un" + me + ":untracked";
        await writer.StringSetAsync(tracked, "v1");
        await writer.StringSetAsync(untracked, "v1");

        var db = muxer.GetDatabase();
        await PrimeAsync(db, cache, tracked, "v1");

        // a repeat read is served locally: nothing new is stored
        var stored = cache.Stored;
        Assert.Equal("v1", await db.Strings.Get(tracked));
        Assert.Equal(stored, cache.Stored);

        // the untracked key is outside the PREFIX the server agreed to announce, so it is never cached at
        // all: a hit there could only ever be retired by the lifetime, with nothing able to say it is wrong
        // sooner. It reads correctly every time, straight from the server.
        Assert.Equal("v1", await db.Strings.Get(untracked));
        Assert.Equal(1, cache.RefusedNotTracked);

        await writer.StringSetAsync(untracked, "v2");
        await writer.StringSetAsync(tracked, "v2");

        // the tracked key is announced, so the entry goes and the next read refetches...
        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.Get(tracked) == "v2"),
            "the invalidation for the tracked key never arrived");

        // ...and the untracked one was never stale, because it was never stored
        Assert.Equal("v2", await db.Strings.Get(untracked));
        Assert.Equal(2, cache.RefusedNotTracked); // both reads of it, refused both times
    }

    [Fact]
    public async Task AFlushPushEmptiesTheWholeCache()
    {
        var me = Me();
        var dbId = TestConfig.GetDedicatedDB();
        var (muxer, cache) = await TrackedAsync(me, database: dbId);
        using var _ = muxer;

        using var other = await ConnectionMultiplexer.ConnectAsync(
            new ConfigurationOptions
            {
                EndPoints = { { TestConfig.Current.PrimaryServer, TestConfig.Current.PrimaryPort } },
                AllowAdmin = true,
            },
            Writer);
        var writer = other.GetDatabase(dbId);

        RedisKey key = me + ":flushed";
        await writer.StringSetAsync(key, "v1");

        var db = muxer.GetDatabase();
        await PrimeAsync(db, cache, key, "v1");

        // nothing writes the key from here on, so the only thing that can dislodge this entry is the null
        // payload being understood as "everything you have is gone". Without that, the value is ours for
        // the whole lifetime and the read below keeps saying "v1" long after the server has forgotten it.
        var server = other.GetServer(TestConfig.Current.PrimaryServerAndPort);
        await server.FlushDatabaseAsync(dbId); // a dedicated database: FLUSHDB on the shared one would take the suite with it

        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.Get(key) is null),
            "the flush push never arrived, or did not empty the cache");
    }
}
