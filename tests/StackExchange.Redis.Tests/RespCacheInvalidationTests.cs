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
    /// that traffic. It also gives the test a control - a key outside the prefix is cached but never
    /// announced, so it can show the cache is genuinely serving rather than quietly missing.
    /// </remarks>
    private async Task<(ConnectionMultiplexer Muxer, RespClientCache Cache)> TrackedAsync(
        string prefix,
        CachePolicy? policy = null,
        int? database = null)
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { { TestConfig.Current.PrimaryServer, TestConfig.Current.PrimaryPort } },
            Protocol = RedisProtocol.Resp3,
            ClientCache = policy ?? new CachePolicy(),
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

        var db = muxer.GetDatabase();
        var reply = await db.ExecuteAsync("CLIENT", "TRACKING", "ON", "BCAST", "PREFIX", prefix);
        Assert.Equal("OK", reply.ToString());

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
        await PrimeAsync(db, cache, untracked, "v1");

        // a repeat read is served locally: nothing new is stored
        var stored = cache.Stored;
        Assert.Equal("v1", await db.Strings.Get(tracked));
        Assert.Equal("v1", await db.Strings.Get(untracked));
        Assert.Equal(stored, cache.Stored);

        await writer.StringSetAsync(untracked, "v2");
        await writer.StringSetAsync(tracked, "v2");

        // the tracked key is announced, so the entry goes and the next read refetches...
        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.Get(tracked) == "v2"),
            "the invalidation for the tracked key never arrived");

        // ...while the untracked one is outside the PREFIX filter, so nothing is ever said about it and we
        // keep serving the value we have. That is the cache proving it was in the path all along - without
        // it, this read would have gone to the server and come back "v2" like the other one.
        Assert.Equal("v1", await db.Strings.Get(untracked));
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

        // deliberately outside the tracking prefix: a flush is the one invalidation PREFIX cannot filter,
        // so if this entry goes, it went because the null payload was understood as "everything you have".
        RedisKey key = "un" + me + ":flushed";
        await writer.StringSetAsync(key, "v1");

        var db = muxer.GetDatabase();
        await PrimeAsync(db, cache, key, "v1");

        await writer.StringSetAsync(key, "v2");
        Assert.Equal("v1", await db.Strings.Get(key)); // still ours; no push could have named it

        var server = other.GetServer(TestConfig.Current.PrimaryServerAndPort);
        await server.FlushDatabaseAsync(dbId); // a dedicated database: FLUSHDB on the shared one would take the suite with it

        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.Get(key) is null),
            "the flush push never arrived, or did not empty the cache");
    }
}
