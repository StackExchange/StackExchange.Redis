using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The invalidation strategies that do not use <c>CLIENT TRACKING</c>: <see cref="CacheTrackingMode.None"/>, retired by
/// lifetime and local writes alone, and <see cref="CacheTrackingMode.KeyspaceNotifications"/>, fed by pub/sub.
/// </summary>
/// <remarks>
/// Both run over RESP2 here on purpose: not needing RESP3 is half of why either exists. The test servers are
/// configured with <c>notify-keyspace-events AKE</c>; nothing here changes that, since it is server-wide.
/// </remarks>
public class RespCacheInvalidationStrategyTests(ITestOutputHelper output) : TestBase(output)
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

    private async Task<ConnectionMultiplexer> ConnectAsync(string configuration, CacheOptions cacheOptions)
    {
        var options = ConfigurationOptions.Parse(configuration);
        options.Protocol = RedisProtocol.Resp2;
        options.ClientCache = cacheOptions;
        try
        {
            return await ConnectionMultiplexer.ConnectAsync(options, Writer);
        }
        catch (RedisConnectionException ex)
        {
            Assert.Skip("Unable to connect to server: " + ex.Message);
            throw;
        }
    }

    /// <summary>Read a key until the reply actually lands in the cache.</summary>
    /// <remarks>
    /// As under <c>BCAST</c> (see <c>RespCacheInvalidationTests.PrimeAsync</c>): the notification for the setup
    /// write can overtake the reply being filled from, and the fill is then rightly refused as raced. Not in
    /// <see cref="CacheTrackingMode.None"/>, where nothing arrives, so one read is enough there.
    /// </remarks>
    private static async Task PrimeAsync(IDatabase db, RespClientCache cache, RedisKey key, string expected)
    {
        var stored = cache.Stored;
        Assert.True(
            await WaitFor(async () =>
            {
                Assert.Equal(expected, (string?)await db.Strings.GetAsync(key));
                return cache.Stored > stored;
            }),
            $"'{key}' was never cached: refusedNotTracked={cache.RefusedNotTracked} raced={cache.RefusedRaced}");
    }

    [Fact]
    public void PrefixesAreAcceptedByTheModesThatReadThem()
    {
        foreach (var mode in new[] { CacheTrackingMode.None, CacheTrackingMode.KeyspaceNotifications })
        {
            using var cache = new RespClientCache(new CacheOptions { TrackingMode = mode, Prefixes = ["a:", "b:"] });
            Assert.True(cache.Options.HasPrefixes);
            Assert.False(cache.Options.UsesClientTracking);
        }
    }

    [Fact]
    public void ZeroIsNoneButAnUnsetModeIsTheLibrarysChoice()
    {
        Assert.Equal(CacheTrackingMode.None, default(CacheTrackingMode));
        Assert.Equal(CacheTrackingMode.Default, new CacheOptions().TrackingMode);
        Assert.Equal(CacheTrackingMode.Default, CacheOptions.Default.TrackingMode);
        Assert.True(new CacheOptions().UsesClientTracking);
    }

    [Fact]
    public void TrackingModesStillNegotiateClientTracking()
    {
        foreach (var mode in new[] { CacheTrackingMode.Default, CacheTrackingMode.Broadcast, CacheTrackingMode.Tracking, CacheTrackingMode.TrackingOptIn })
        {
            Assert.True(new CacheOptions { TrackingMode = mode }.UsesClientTracking, mode.ToString());
        }
    }

    [Theory]
    [InlineData("a*")]
    [InlineData("a?")]
    [InlineData("a[b]")]
    public void KeyspacePrefixesMustNotBePatterns(string prefix)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new RespClientCache(new CacheOptions { TrackingMode = CacheTrackingMode.KeyspaceNotifications, Prefixes = [prefix] }));
        Assert.Equal(nameof(CacheOptions.Prefixes), ex.ParamName);

        // the same prefix means nothing special when nothing is subscribed
        using var _ = new RespClientCache(new CacheOptions { TrackingMode = CacheTrackingMode.None, Prefixes = [prefix] });
    }

    /// <summary>
    /// <see cref="CacheTrackingMode.None"/> hears nothing from the server: a write elsewhere is served stale until the
    /// lifetime retires it, while a write through this multiplexer is seen at once.
    /// </summary>
    [Fact]
    public async Task NoneServesAnotherClientsWriteStaleButSeesItsOwn()
    {
        var me = Me();
        using var muxer = await ConnectAsync(
            TestConfig.Current.PrimaryServerAndPort,
            new CacheOptions { TrackingMode = CacheTrackingMode.None, Admission = CacheAdmission.OnFirstMiss });
        var cache = muxer.ClientCache;
        Assert.NotNull(cache);

        using var other = await ConnectionMultiplexer.ConnectAsync(TestConfig.Current.PrimaryServerAndPort, Writer);
        var writer = other.GetDatabase();
        var db = muxer.GetDatabase();
        RedisKey key = me;

        await writer.StringSetAsync(key, "v1");
        await PrimeAsync(db, cache, key, "v1");

        await writer.StringSetAsync(key, "v2");
        await Task.Delay(200); // long enough for any invalidation to have arrived, were one coming
        Assert.Equal("v1", (string?)await db.Strings.GetAsync(key));

        await db.StringSetAsync(key, "v3");
        Assert.Equal("v3", (string?)await db.Strings.GetAsync(key));
    }

    /// <summary>
    /// <see cref="CacheTrackingMode.KeyspaceNotifications"/> over RESP2: a write elsewhere invalidates through the
    /// keyspace channel, with and without prefixes.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyspaceNotificationsInvalidateAnotherClientsWrite(bool withPrefix)
    {
        var me = Me() + ":" + withPrefix + ":";
        using var muxer = await ConnectAsync(
            TestConfig.Current.PrimaryServerAndPort,
            new CacheOptions
            {
                TrackingMode = CacheTrackingMode.KeyspaceNotifications,
                Admission = CacheAdmission.OnFirstMiss,
                Prefixes = withPrefix ? [me] : [],
            });
        await AssertInvalidatedAsync(muxer, TestConfig.Current.PrimaryServerAndPort, me, keyCount: 1);
    }

    /// <summary>
    /// The notification is raised only by the node that owns the key, so the subscription has to be on every
    /// node; keys spread over the slots prove that it is.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyspaceNotificationsInvalidateOnEveryClusterNode(bool withPrefix)
    {
        var me = Me() + ":" + withPrefix + ":";
        var config = GetClusterConfiguration();
        using var muxer = await ConnectAsync(
            config,
            new CacheOptions
            {
                TrackingMode = CacheTrackingMode.KeyspaceNotifications,
                Admission = CacheAdmission.OnFirstMiss,
                Prefixes = withPrefix ? [me] : [],
            });
        Assert.SkipUnless(muxer.ServerSelectionStrategy.ServerType == ServerType.Cluster, "not a cluster");
        await AssertInvalidatedAsync(muxer, config, me, keyCount: 20);
    }

    /// <summary>A key outside the prefixes is never announced, so it is never cached.</summary>
    [Fact]
    public async Task KeyspaceNotificationsRefuseAKeyOutsideThePrefixes()
    {
        var me = Me() + ":";
        using var muxer = await ConnectAsync(
            TestConfig.Current.PrimaryServerAndPort,
            new CacheOptions { TrackingMode = CacheTrackingMode.KeyspaceNotifications, Admission = CacheAdmission.OnFirstMiss, Prefixes = [me] });
        var cache = muxer.ClientCache;
        Assert.NotNull(cache);

        var db = muxer.GetDatabase();
        RedisKey outside = "un" + me;
        await db.StringSetAsync(outside, "v1");
        Assert.Equal("v1", (string?)await db.Strings.GetAsync(outside));
        Assert.Equal(1, cache.RefusedNotTracked);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("K", false)] // the channel, but no events on it
    [InlineData("Kn", false)] // new keys have nothing cached
    [InlineData("Km", false)] // misses are reads
    [InlineData("AE", false)] // keyevent only
    [InlineData("KA", true)]
    [InlineData("AKE", true)]
    [InlineData("K$", true)]
    [InlineData("Kgx", true)]
    public void KeyspaceEventsThatCanInvalidate(string events, bool expected)
        => Assert.Equal(expected, CacheOptions.KeyspaceEventsInvalidate(events));

    /// <summary>A disabled <c>CLIENT</c> is known before connecting: Default starts out on keyspace notifications.</summary>
    [Fact]
    public void DefaultFallsBackAtOnceWhenClientIsDisabled()
    {
        var map = CommandMap.Create(["CLIENT"], available: false);
        using var fallen = new RespClientCache(new CacheOptions(), commandMap: map);
        Assert.Equal(CacheTrackingMode.KeyspaceNotifications, fallen.EffectiveTrackingMode);
        Assert.False(fallen.UsesClientTracking);

        // a named mode is a requirement, and is not quietly changed
        Assert.Throws<RedisCommandException>(() => new RespClientCache(new CacheOptions { TrackingMode = CacheTrackingMode.Tracking }, commandMap: map));
    }

    /// <summary>
    /// The switch is whole: the <c>CLIENT CACHING</c> frames stop at once - a server that is not tracking rejects
    /// them - and the prefixes become the hard boundary they are under keyspace notifications.
    /// </summary>
    [Fact]
    public void FallingBackStopsTheClientCachingFramesAndHappensOnce()
    {
        using var optOut = new RespClientCache(new CacheOptions());
        Assert.Equal(CacheTrackingMode.Tracking, optOut.EffectiveTrackingMode);
        Assert.NotNull(optOut.OptOutPrefix(CommandFlags.CacheOptOut | CommandFlags.CommandRetryReadOnly));

        Assert.True(optOut.TryFallBackToKeyspaceNotifications());
        Assert.False(optOut.TryFallBackToKeyspaceNotifications());
        Assert.Equal(CacheTrackingMode.KeyspaceNotifications, optOut.EffectiveTrackingMode);
        Assert.Equal(CacheTrackingMode.KeyspaceNotifications, optOut.View.EffectiveTrackingMode);
        Assert.Null(optOut.OptOutPrefix(CommandFlags.CacheOptOut | CommandFlags.CommandRetryReadOnly));

        using var byPrefix = new RespClientCache(new CacheOptions { Prefixes = ["app:"] });
        Assert.Equal(CacheTrackingMode.TrackingByPrefix, byPrefix.EffectiveTrackingMode);
        Assert.NotNull(byPrefix.FillPrefix);
        Assert.True(byPrefix.TryFallBackToKeyspaceNotifications());
        Assert.Null(byPrefix.FillPrefix);

        // only Default may fall back
        using var named = new RespClientCache(new CacheOptions { TrackingMode = CacheTrackingMode.Tracking });
        Assert.False(named.TryFallBackToKeyspaceNotifications());
        Assert.Equal(CacheTrackingMode.Tracking, named.EffectiveTrackingMode);
    }

    /// <summary>
    /// Default over RESP2 used to refuse to connect; it now connects on keyspace notifications, and they work.
    /// </summary>
    [Fact]
    public async Task DefaultOverResp2FallsBackToKeyspaceNotifications()
    {
        var me = Me() + ":";
        using var muxer = await ConnectAsync(TestConfig.Current.PrimaryServerAndPort, new CacheOptions { Admission = CacheAdmission.OnFirstMiss, Prefixes = [me] });
        Assert.Equal(CacheTrackingMode.KeyspaceNotifications, muxer.GetClientCache()?.EffectiveTrackingMode);
        await AssertInvalidatedAsync(muxer, TestConfig.Current.PrimaryServerAndPort, me, keyCount: 1);
    }

    /// <summary>...whereas a named tracking mode over RESP2 still refuses, as it always has.</summary>
    [Fact]
    public async Task ANamedTrackingModeOverResp2StillRefuses()
    {
        var options = ConfigurationOptions.Parse(TestConfig.Current.PrimaryServerAndPort);
        options.Protocol = RedisProtocol.Resp2;
        options.AbortOnConnectFail = true;
        options.ClientCache = new CacheOptions { TrackingMode = CacheTrackingMode.Tracking };
        await Assert.ThrowsAnyAsync<RedisConnectionException>(() => ConnectionMultiplexer.ConnectAsync(options, Writer));
    }

    /// <summary>
    /// A server that refuses <c>CLIENT TRACKING</c> - an old server, a proxy, an ACL - makes Default fall back, and a
    /// named mode fail. The fallback is logged, and so - when <c>CONFIG GET</c> can say - are notifications that are
    /// off; a diagnostic, never a refusal.
    /// </summary>
    [Theory]
    [InlineData("", true)]
    [InlineData("K", true)]
    [InlineData("KA", false)]
    [InlineData(null, false)] // the server does not say: nothing to warn about
    public async Task AServerThatRefusesTrackingMakesDefaultFallBack(string? events, bool warnsEventsOff)
    {
        using var server = new NoTrackingServer(Output, events);
        var config = server.GetClientConfig();
        config.Protocol = RedisProtocol.Resp3;
        config.ClientCache = new CacheOptions();
        var logger = new CapturingLogger();
        config.LoggerFactory = logger;
        using (var muxer = await ConnectionMultiplexer.ConnectAsync(config, Writer))
        {
            var cache = muxer.GetClientCache();
            Assert.NotNull(cache);
            Assert.Equal(CacheTrackingMode.KeyspaceNotifications, cache.EffectiveTrackingMode);
            Assert.True(muxer.IsConnected);
            await muxer.GetDatabase().PingAsync();
        }

        Output.WriteLine(logger.All);
        Assert.Single(logger.Matching("has switched to keyspace notifications"));
        Assert.Equal(warnsEventsOff, logger.Matching("notify-keyspace-events is").Any());

        config = server.GetClientConfig();
        config.Protocol = RedisProtocol.Resp3;
        config.AbortOnConnectFail = true;
        config.ClientCache = new CacheOptions { TrackingMode = CacheTrackingMode.Tracking };
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectionMultiplexer.ConnectAsync(config, Writer));
    }

    private async Task AssertInvalidatedAsync(ConnectionMultiplexer muxer, string configuration, string prefix, int keyCount)
    {
        var cache = muxer.ClientCache;
        Assert.NotNull(cache);

        // somebody else, with no cache, whose writes we must hear about
        using var other = await ConnectionMultiplexer.ConnectAsync(configuration, Writer);
        var writer = other.GetDatabase();
        var db = muxer.GetDatabase();

        var keys = new RedisKey[keyCount];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = prefix + i;
            await writer.StringSetAsync(keys[i], "v1");
            await PrimeAsync(db, cache, keys[i], "v1");
        }

        foreach (var key in keys)
        {
            await writer.StringSetAsync(key, "v2");
        }

        foreach (var key in keys)
        {
            Assert.True(
                await WaitFor(async () => (string?)await db.Strings.GetAsync(key) == "v2"),
                $"the keyspace notification for '{key}' never invalidated it");
        }
    }

    /// <summary>The toy server, answering <c>CLIENT TRACKING</c> as a server without it would.</summary>
    private sealed class NoTrackingServer : InProcessTestServer
    {
        public NoTrackingServer(ITestOutputHelper log, string? keyspaceEvents = null) : base(log)
        {
            // a struct over the server's dictionary: the copy writes through
            var configuration = ServerConfiguration;
            if (keyspaceEvents is not null) configuration["notify-keyspace-events"] = keyspaceEvents;
        }

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
            => request.Count >= 2 && request.IsString(0, "CLIENT"u8) && request.IsString(1, "TRACKING"u8)
                ? TypedRedisValue.Error("ERR unknown subcommand 'TRACKING'")
                : base.Execute(client, in request);
    }
}
