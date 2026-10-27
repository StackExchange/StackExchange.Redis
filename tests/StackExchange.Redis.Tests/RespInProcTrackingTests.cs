using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using StackExchange.Redis.Caching;
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
    private static async Task PrimeAsync(IDatabase db, RespClientCache cache, string key, string expected, int expectedCount = 1, CommandFlags flags = CommandFlags.None)
    {
        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.GetAsync(key, flags) == expected && cache.Count == expectedCount),
            $"'{key}' never stayed in the cache (stored: {cache.Stored}, raced: {cache.RefusedRaced}, count: {cache.Count})");
    }

    /// <summary>The one client on <paramref name="server"/> that negotiated tracking.</summary>
    private static RedisClient TrackedClient(InProcessTestServer server)
    {
        var tracked = new List<RedisClient>();
        server.ForAllClients(client =>
        {
            if (client.TrackingEnabled) tracked.Add(client);
        });
        return Assert.Single(tracked);
    }

    private async Task<(InProcessTestServer Server, ConnectionMultiplexer Muxer, RespClientCache Cache)> ConnectAsync(
        CacheOptions? options = null)
    {
        var server = new InProcessTestServer(log);
        var config = server.GetClientConfig();
        config.Protocol = RedisProtocol.Resp3;
        config.ClientCache = options ?? new CacheOptions { Admission = CacheAdmission.OnFirstMiss };

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
        // Broadcast by name: it is the mode that sends the list to the server, and no longer what prefixes
        // alone select (that is TrackingByPrefix, which keeps them here)
        var (server, muxer, cache) = await ConnectAsync(new CacheOptions { Admission = CacheAdmission.OnFirstMiss, TrackingMode = CacheTrackingMode.Broadcast, Prefixes = ["app:"] });
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
    /// Tracking mode is the other half of <see cref="CacheTrackingMode"/>, and the default: no BCAST, and
    /// the server remembers what this connection read rather than being told what to watch.
    /// </summary>
    /// <remarks>
    /// Worth its own test because the two modes fail differently. Broadcast over-delivers; a tracking
    /// registration is consumed when it fires, so it is only sound while every cached entry still has a live
    /// registration behind it - which holds here because an invalidation evicts, and the next read is
    /// therefore a server read that re-registers. Run twice: once naming the mode, once leaving it at
    /// <see cref="CacheTrackingMode.Default"/>, which must resolve to the same thing.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TrackingModeAsksForNoBroadcastAndStillInvalidates(bool explicitMode)
    {
        var (server, muxer, cache) = await ConnectAsync(
            new CacheOptions { Admission = CacheAdmission.OnFirstMiss, TrackingMode = explicitMode ? CacheTrackingMode.Tracking : CacheTrackingMode.Default });
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
        Assert.False(Assert.Single(tracked).TrackingBroadcast, "tracking mode must not send BCAST");

        await using var other = await ConnectionMultiplexer.ConnectAsync(server.GetClientConfig());
        await other.GetDatabase().StringSetAsync("k", "v2");

        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.GetAsync("k") == "v2"),
            "the per-key invalidation never arrived");
    }

    /// <summary>
    /// The public view reaches the connection's cache, and an invalidation through it makes the next read a
    /// server read; without a cache configured there is nothing to reach.
    /// </summary>
    [Fact]
    public async Task GetClientCacheReachesTheConnectionsCache()
    {
        var (server, muxer, cache) = await ConnectAsync();
        using var _ = server;
        await using var __ = muxer;

        var view = muxer.GetClientCache();
        Assert.NotNull(view);
        Assert.Same(view, muxer.GetClientCache());

        var db = muxer.GetDatabase();
        await db.StringSetAsync("k", "v1");
        await PrimeAsync(db, cache, "k", "v1");
        Assert.Equal(cache.Count, view.Count);
        Assert.Equal(cache.Stored, view.Stored);

        var stored = cache.Stored;
        Assert.True(view.Invalidate((RedisKey)"k"));
        Assert.Equal("v1", await db.Strings.GetAsync("k")); // the entry was dropped, so this went to the server...
        Assert.True(cache.Stored > stored, "...and was stored again");

        await using var plain = await ConnectionMultiplexer.ConnectAsync(server.GetClientConfig());
        Assert.Null(plain.GetClientCache());
    }

    /// <summary>A flush is the invalidation no prefix can filter.</summary>
    [Fact]
    public async Task AFlushEmptiesEverything()
    {
        var (server, muxer, cache) = await ConnectAsync(new CacheOptions { Admission = CacheAdmission.OnFirstMiss, Prefixes = ["app:"] });
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
    /// Without RESP3 a named tracking mode fails the connection rather than quietly caching without invalidation.
    /// </summary>
    /// <remarks>
    /// The one case where taking the connection down is the kinder outcome: a cache that fills and is never
    /// invalidated is silently and durably wrong, bounded only by the entry lifetime. A mode left at
    /// <see cref="CacheTrackingMode.Default"/> falls back to keyspace notifications instead - see
    /// <c>RespCacheInvalidationStrategyTests.DefaultOverResp2FallsBackToKeyspaceNotifications</c>.
    /// </remarks>
    [Fact]
    public async Task Resp2WithANamedTrackingModeRefusesLoudly()
    {
        using var server = new InProcessTestServer(log);
        var config = server.GetClientConfig();
        config.Protocol = RedisProtocol.Resp2;
        config.ClientCache = new CacheOptions { Admission = CacheAdmission.OnFirstMiss, TrackingMode = CacheTrackingMode.Tracking };
        config.AbortOnConnectFail = true;

        var connectLog = new StringWriter();
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ConnectionMultiplexer.ConnectAsync(config, connectLog));

        // the reason has to reach a human somewhere: either chained onto the connect failure, or in the
        // connect log - which is the artifact users actually paste into issues
        var reported = ex.ToString() + Environment.NewLine + connectLog;
        log.WriteLine(reported);
        Assert.Contains("RESP3", reported);
    }

    // ---- opt-in / opt-out, against the server's own record of what it was told ----

    [Fact]
    public async Task TrackingModeNegotiatesOptOut()
    {
        // OPTOUT is identical to plain ON except that it makes CLIENT CACHING NO legal, which is what the
        // CacheOptOut flag's wire half needs; so that is what the handshake asks for
        var (server, muxer, cache) = await ConnectAsync();
        using var _ = server;
        await using var __ = muxer;

        var db = muxer.GetDatabase();
        await db.StringSetAsync("k", "v1");
        await PrimeAsync(db, cache, "k", "v1");

        var client = TrackedClient(server);
        Assert.True(client.TrackingOptOut, "Tracking must negotiate OPTOUT");
        Assert.False(client.TrackingOptIn);
        Assert.False(client.TrackingBroadcast);
        Assert.True(client.IsTrackingKey(0, "k"));
    }

    [Fact]
    public async Task OptInModeCachesAndTracksOnlyWhatOptedIn()
    {
        // default admission on purpose: CacheOptIn has to be stored on its FIRST miss, since that is the
        // read that paid for CLIENT CACHING YES
        var (server, muxer, cache) = await ConnectAsync(new CacheOptions { TrackingMode = CacheTrackingMode.TrackingOptIn });
        using var _ = server;
        await using var __ = muxer;

        var db = muxer.GetDatabase();
        await db.StringSetAsync("k", "v1");

        // a default read: answered by the server, kept nowhere, tracked nowhere
        Assert.Equal("v1", await db.Strings.GetAsync("k"));
        Assert.Equal("v1", await db.Strings.GetAsync("k"));
        Assert.Equal(0, cache.Count);
        var client = TrackedClient(server);
        Assert.True(client.TrackingOptIn, "TrackingOptIn must negotiate OPTIN");
        Assert.Equal(0, client.CachingYesCount);
        Assert.False(client.IsTrackingKey(0, "k"));

        // an opted-in read: CLIENT CACHING YES went out ahead of it, the server tracks it, the cache keeps it
        await PrimeAsync(db, cache, "k", "v1", flags: CommandFlags.CacheOptIn);
        Assert.True(client.CachingYesCount >= 1, "the server never saw CLIENT CACHING YES");
        Assert.True(client.IsTrackingKey(0, "k"), "the server is not tracking the opted-in key");

        var yes = client.CachingYesCount;
        var stored = cache.Stored;
        Assert.Equal("v1", await db.Strings.GetAsync("k", CommandFlags.CacheOptIn)); // a hit: nothing sent
        Assert.Equal(yes, client.CachingYesCount);
        Assert.Equal(stored, cache.Stored);

        // and the invalidation loop closes as it does in any tracking mode
        await using var other = await ConnectionMultiplexer.ConnectAsync(server.GetClientConfig());
        await other.GetDatabase().StringSetAsync("k", "v2");
        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.GetAsync("k", CommandFlags.CacheOptIn) == "v2"),
            "the invalidation for an opted-in key never arrived");
    }

    [Fact]
    public async Task ByPrefixModeNegotiatesOptInAndTheListStaysHere()
    {
        // prefixes alone select this mode; default admission on purpose, since a read inside the prefixes is
        // an opt-in and must be stored on the miss that paid for CLIENT CACHING YES
        var (server, muxer, cache) = await ConnectAsync(new CacheOptions { Prefixes = ["app:"] });
        using var _ = server;
        await using var __ = muxer;

        var db = muxer.GetDatabase();
        await db.StringSetAsync("app:k", "v1");
        await db.StringSetAsync("other", "o");

        // outside the prefixes, unflagged: answered by the server, kept nowhere, tracked nowhere
        Assert.Equal("o", await db.Strings.GetAsync("other"));
        Assert.Equal("o", await db.Strings.GetAsync("other"));
        Assert.Equal(0, cache.Count);
        var client = TrackedClient(server);
        Assert.True(client.TrackingOptIn, "TrackingByPrefix must negotiate OPTIN");
        Assert.False(client.TrackingBroadcast, "PREFIX would be refused without BCAST; the list must stay client-side");
        Assert.Empty(client.TrackingPrefixes);
        Assert.Equal(0, client.CachingYesCount);
        Assert.False(client.IsTrackingKey(0, "other"));

        // inside, unflagged: YES went out ahead of it, the server tracks it, the cache keeps it on first miss
        await PrimeAsync(db, cache, "app:k", "v1");
        Assert.True(client.CachingYesCount >= 1, "the server never saw CLIENT CACHING YES");
        Assert.True(client.IsTrackingKey(0, "app:k"), "the server is not tracking the key inside the prefixes");

        var yes = client.CachingYesCount;
        var stored = cache.Stored;
        Assert.Equal("v1", await db.Strings.GetAsync("app:k")); // a hit: nothing sent
        Assert.Equal(yes, client.CachingYesCount);
        Assert.Equal(stored, cache.Stored);

        // the flags win over the list: outside with CacheOptIn is tracked and kept...
        await PrimeAsync(db, cache, "other", "o", expectedCount: 2, flags: CommandFlags.CacheOptIn);
        Assert.True(client.IsTrackingKey(0, "other"), "an opted-in key outside the prefixes must still be tracked");

        // ...and inside with CacheOptOut is neither, with nothing extra on the wire (OPTIN has no NO to say)
        await db.StringSetAsync("app:j", "j");
        Assert.Equal("j", await db.Strings.GetAsync("app:j", CommandFlags.CacheOptOut));
        Assert.Equal(2, cache.Count);
        Assert.Equal(0, client.CachingNoCount);
        Assert.False(client.IsTrackingKey(0, "app:j"));

        // and the invalidation loop closes for a key opted in by prefix
        await using var other = await ConnectionMultiplexer.ConnectAsync(server.GetClientConfig());
        await other.GetDatabase().StringSetAsync("app:k", "v2");
        Assert.True(
            await WaitFor(async () => (string?)await db.Strings.GetAsync("app:k") == "v2"),
            "the invalidation for a key opted in by prefix never arrived");
    }

    [Fact]
    public async Task OptOutFlagTellsATrackingServerNotToTrack()
    {
        var (server, muxer, cache) = await ConnectAsync();
        using var _ = server;
        await using var __ = muxer;

        var db = muxer.GetDatabase();
        await db.StringSetAsync("k", "v1");

        Assert.Equal("v1", await db.Strings.GetAsync("k", CommandFlags.CacheOptOut));
        Assert.Equal(0, cache.Count);
        var client = TrackedClient(server);
        Assert.Equal(1, client.CachingNoCount);
        Assert.False(client.IsTrackingKey(0, "k"), "an opted-out read must not leave a tracking entry behind");

        // the same key read normally is tracked and kept, with nothing extra on the wire
        await PrimeAsync(db, cache, "k", "v1");
        Assert.True(client.IsTrackingKey(0, "k"));
        Assert.Equal(1, client.CachingNoCount);
    }

    [Theory]
    [InlineData(CacheTrackingMode.Broadcast)]
    [InlineData(CacheTrackingMode.TrackingOptIn)]
    public async Task OptOutFlagSaysNothingWhereTheServerWouldRefuseIt(CacheTrackingMode mode)
    {
        // BCAST has no per-client table to opt out of, and OPTIN tracks nothing unless asked; CLIENT CACHING
        // NO would be an error on both, so the flag keeps only its local meaning
        var (server, muxer, cache) = await ConnectAsync(new CacheOptions { TrackingMode = mode });
        using var _ = server;
        await using var __ = muxer;

        var db = muxer.GetDatabase();
        await db.StringSetAsync("k", "v1");
        Assert.Equal("v1", await db.Strings.GetAsync("k", CommandFlags.CacheOptOut));
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, TrackedClient(server).CachingNoCount);
    }

    [Fact]
    public async Task ATransactionThatReadsOptsTheWholeTransactionOut()
    {
        // reads inside MULTI are never cached, so under OPTOUT the MULTI goes out behind CLIENT CACHING NO
        // and the server tracks none of the transaction's keys; a write-only transaction gets nothing
        var (server, muxer, cache) = await ConnectAsync();
        using var _ = server;
        await using var __ = muxer;

        var db = muxer.GetDatabase();
        await db.StringSetAsync("k", "v1");
        var client = TrackedClient(server);

        var tran = db.CreateTransaction();
        var read = tran.StringGetAsync("k");
        var write = tran.StringSetAsync("w", "x");
        Assert.True(await tran.ExecuteAsync());
        Assert.Equal("v1", await read);
        await write;
        Assert.Equal(1, client.CachingNoCount);
        Assert.False(client.IsTrackingKey(0, "k"), "a read inside MULTI must not leave a tracking entry behind");
        Assert.Equal(0, cache.Count);

        var writeOnly = db.CreateTransaction();
        var only = writeOnly.StringSetAsync("w", "y");
        Assert.True(await writeOnly.ExecuteAsync());
        await only;
        Assert.Equal(1, client.CachingNoCount); // nothing to opt out of

        // the key is still perfectly cacheable outside a transaction
        await PrimeAsync(db, cache, "k", "v1");
        Assert.True(client.IsTrackingKey(0, "k"));
    }

    [Fact]
    public async Task AnInvalidationBetweenThePrefixReplyAndTheReadReplyIsRoutedNotMatched()
    {
        // the server may push an invalidation at any point, including between the +OK for CLIENT CACHING
        // YES and the reply to the GET behind it. A push is routed before anything is dequeued, so neither
        // the prefix's discard nor the read's own entry may be consumed by it - and every reply after it
        // must still land on its own command. Forced here, because a real server cannot be timed.
        var (server, muxer, cache) = await ConnectAsync(new CacheOptions { TrackingMode = CacheTrackingMode.TrackingOptIn });
        using var _ = server;
        await using var __ = muxer;

        var db = muxer.GetDatabase();
        await db.StringSetAsync("k", "v1");
        await db.StringSetAsync("other", "o");

        var pushes = 0;
        server.AfterReply = (client, command, subcommand) =>
        {
            if (command == RedisCommand.CLIENT && string.Equals(subcommand, "CACHING", StringComparison.OrdinalIgnoreCase))
            {
                pushes++;
                // the key being read, so the fill is refused as raced - the correct answer - and the result
                // still has to be the GET's reply, not the push and not the +OK
                server.SendInvalidation(client, [(RedisKey)"k"]);
            }
        };

        Assert.Equal("v1", await db.Strings.GetAsync("k", CommandFlags.CacheOptIn));
        Assert.True(pushes >= 1, "the hook never fired; the prefix was not sent");
        Assert.Equal(0, cache.Count);
        Assert.True(cache.RefusedRaced >= 1, "the invalidation should have refused the fill");

        // the connection is still in step: an unrelated read, then the opted-in read again with no push
        Assert.Equal("o", await db.Strings.GetAsync("other"));
        server.AfterReply = null;
        await PrimeAsync(db, cache, "k", "v1", flags: CommandFlags.CacheOptIn);
        Assert.Equal("v1", await db.Strings.GetAsync("k", CommandFlags.CacheOptIn));
    }
}
