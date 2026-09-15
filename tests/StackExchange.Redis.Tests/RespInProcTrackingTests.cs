using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using StackExchange.Redis.Interpolated;
using StackExchange.Redis.Server;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Client-side caching against the in-process server, with the <b>library</b> turning tracking on.
/// </summary>
/// <remarks>
/// <para>
/// <c>RespCacheInvalidationTests</c> proves the same loop against a real server. It used to issue
/// <c>CLIENT TRACKING</c> itself - testing the server and the push routing, but saying nothing about the
/// code that would actually run - and no longer does; the handshake is now the only thing that sends that
/// command anywhere. These tests are where that is asserted directly, including what went on the wire,
/// which the real-server tests cannot see.
/// </para>
/// <para>
/// The in-process server also buys isolation. The real-server suite has to serialise its tracking classes
/// because a <c>FLUSHDB</c> broadcasts an unfilterable push to every tracking client on the box; here each
/// test owns its server.
/// </para>
/// </remarks>
public class RespInProcTrackingTests(ITestOutputHelper log)
{
    private static async Task<bool> WaitFor(Func<Task<bool>> condition, int millis = 2000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < millis)
        {
            if (await condition()) return true;
            await Task.Delay(25);
        }

        return await condition();
    }

    /// <summary>Read <paramref name="key"/> until the reply actually sticks in the cache.</summary>
    /// <remarks>
    /// "Write, read, assert it cached" is a race. A write is announced back to the connection that made it
    /// (NOLOOP is not requested), and that push can land while the first read is still in flight - at which
    /// point refusing the fill is the <i>correct</i> answer, because the reply may predate the change. So
    /// the first read is not guaranteed to stick; reading until one does costs nothing when it sticks first
    /// time, and is the difference between a test that passes alone and one that passes under load.
    /// </remarks>
    private static async Task PrimeAsync(IDatabase db, RespClientCache cache, string key, string expected, int expectedCount = 1)
    {
        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.GetAsync(key) == expected && cache.Count == expectedCount),
            $"'{key}' never stayed in the cache (stored: {cache.Stored}, raced: {cache.RefusedRaced}, count: {cache.Count})");
    }

    private async Task<(InProcessTestServer Server, ConnectionMultiplexer Muxer, RespClientCache Cache)> ConnectAsync(
        CacheOptions? options = null)
    {
        var server = new InProcessTestServer(log);
        var config = server.GetClientConfig();
        config.Protocol = RedisProtocol.Resp3;
        config.ClientCache = options ?? new CacheOptions();

        var muxer = await ConnectionMultiplexer.ConnectAsync(config, new TextWriterOutputHelper(log));
        var cache = muxer.ClientCache;
        Assert.NotNull(cache);
        return (server, muxer, cache);
    }

    [Fact]
    public async Task TheLibraryEnablesTrackingAndInvalidationsArrive()
    {
        var (server, muxer, cache) = await ConnectAsync();
        using var _ = server;
        await using var __ = muxer;

        var db = muxer.GetDatabase();
        await db.StringSetAsync("k", "v1");

        // get it cached, then read again: the second is served locally, which is what makes the third
        // read - after somebody else writes - meaningful
        await PrimeAsync(db, cache, "k", "v1");
        var stored = cache.Stored;
        Assert.Equal("v1", await db.Strings.GetAsync("k"));
        Assert.Equal(stored, cache.Stored);

        // a write from elsewhere; nothing in this test ever sent CLIENT TRACKING
        await using var other = await ConnectionMultiplexer.ConnectAsync(server.GetClientConfig());
        await other.GetDatabase().StringSetAsync("k", "v2");

        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.GetAsync("k") == "v2"),
            "the invalidation never arrived - did the handshake stop sending CLIENT TRACKING?");
    }

    /// <summary>
    /// With prefixes, a key outside them is never cached - so nothing can go stale unnoticed.
    /// </summary>
    /// <remarks>
    /// This also proves the PREFIX arguments reached the server, rather than the client merely believing
    /// it asked for them: an unprefixed key is refused locally, and a prefixed one round-trips.
    /// </remarks>
    [Fact]
    public async Task PrefixesAreSentAndHonoured()
    {
        var (server, muxer, cache) = await ConnectAsync(new CacheOptions { Prefixes = ["app:"] });
        using var _ = server;
        await using var __ = muxer;

        var db = muxer.GetDatabase();
        await db.StringSetAsync("app:k", "v1");
        await db.StringSetAsync("other", "v1");

        await PrimeAsync(db, cache, "app:k", "v1");
        Assert.Equal("v1", await db.Strings.GetAsync("other"));

        Assert.Equal(1, cache.Count);              // only the tracked one is held
        Assert.True(cache.RefusedNotTracked > 0);  // and the other was refused, not cached-and-hoped

        // and now the half the client cannot see: what it actually put on the wire. Without this, dropping
        // PREFIX from the handshake changes nothing above - the client would simply be told about more keys
        // than it asked for, which no assertion on cache contents can notice.
        var tracked = new List<RedisClient>();
        server.ForAllClients(client =>
        {
            if (client.TrackingEnabled) tracked.Add(client);
        });

        var negotiated = Assert.Single(tracked);
        Assert.True(negotiated.TrackingBroadcast, "PREFIX is meaningless without BCAST");
        Assert.Equal(["app:"], negotiated.TrackingPrefixes);
    }

    /// <summary>
    /// Per-key tracking is the other half of <see cref="CacheTrackingMode"/>: no BCAST, and the server
    /// remembers what this connection read rather than being told what to watch.
    /// </summary>
    /// <remarks>
    /// Worth its own test because the two modes fail differently. Broadcast over-delivers; per-key
    /// registration is consumed when it fires, so it is only sound while every cached entry still has a live
    /// registration behind it - which holds here because an invalidation evicts, and the next read is
    /// therefore a server read that re-registers.
    /// </remarks>
    [Fact]
    public async Task PerKeyTrackingAsksForNoBroadcastAndStillInvalidates()
    {
        var (server, muxer, cache) = await ConnectAsync(
            new CacheOptions { TrackingMode = CacheTrackingMode.PerKey });
        using var _ = server;
        await using var __ = muxer;

        var db = muxer.GetDatabase();
        await db.StringSetAsync("k", "v1");

        await PrimeAsync(db, cache, "k", "v1"); // registers the key server-side, and caches it

        var tracked = new List<RedisClient>();
        server.ForAllClients(client =>
        {
            if (client.TrackingEnabled) tracked.Add(client);
        });
        Assert.False(Assert.Single(tracked).TrackingBroadcast, "per-key mode must not send BCAST");

        await using var other = await ConnectionMultiplexer.ConnectAsync(server.GetClientConfig());
        await other.GetDatabase().StringSetAsync("k", "v2");

        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.GetAsync("k") == "v2"),
            "the per-key invalidation never arrived");
    }

    /// <summary>A flush is the invalidation no prefix can filter.</summary>
    [Fact]
    public async Task AFlushEmptiesEverything()
    {
        var (server, muxer, cache) = await ConnectAsync(new CacheOptions { Prefixes = ["app:"] });
        using var _ = server;
        await using var __ = muxer;

        var db = muxer.GetDatabase();
        await db.StringSetAsync("app:k", "v1");
        await PrimeAsync(db, cache, "app:k", "v1");

        await muxer.GetServer(server.DefaultEndPoint).FlushDatabaseAsync();

        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.GetAsync("app:k") is null),
            "the flush push never arrived, or did not empty the cache");
    }

    /// <summary>
    /// Without RESP3 the connection fails rather than quietly caching without invalidation.
    /// </summary>
    /// <remarks>
    /// The one case where taking the connection down is the kinder outcome: a cache that fills and is never
    /// invalidated is silently and durably wrong, bounded only by the entry lifetime.
    /// </remarks>
    [Fact]
    public async Task Resp2WithACacheRefusesLoudly()
    {
        using var server = new InProcessTestServer(log);
        var config = server.GetClientConfig();
        config.Protocol = RedisProtocol.Resp2;
        config.ClientCache = new CacheOptions();
        config.AbortOnConnectFail = true;

        var connectLog = new StringWriter();
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ConnectionMultiplexer.ConnectAsync(config, connectLog));

        // the reason has to reach a human somewhere: either chained onto the connect failure, or in the
        // connect log - which is the artifact users actually paste into issues
        var reported = ex.ToString() + Environment.NewLine + connectLog;
        log.WriteLine(reported);
        Assert.Contains("RESP3", reported);
    }
}
