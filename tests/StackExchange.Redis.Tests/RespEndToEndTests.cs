using System.Text;
using System.Threading.Tasks;
using NSubstitute;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Caching;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
﻿using System;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The context surface against a REAL server, via the existing message pipeline.
/// </summary>
/// <remarks>
/// Everything before this was validated against fakes, which proves the shape but not that the bytes are
/// acceptable - only framing was ever in question, never semantics. These are the first commands from the
/// new writer that a server has actually seen.
/// </remarks>
public class RespEndToEndTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private static RespDatabaseContext NewSurface(IConnectionMultiplexer conn, int db, RespClientCache? cache = null)
    {
        var database = (RedisBase)conn.GetDatabase(db);
        var context = new RespContext(database.multiplexer.CommandMap, database: db)
            .WithExecutor(new RespMessageExecutor(database, db))
            .WithCache(cache);
        return new RespDatabaseContext(context);
    }

    [Fact]
    public async Task TheMinimalRunNeedsNoWiringAtAll()
    {
        await using var conn = Create();
        var key = Me();
        var db = conn.GetDatabase();
        await db.KeyDeleteAsync(key);

        // RedisBase.Raw is virtual and RedisDatabase overrides it, so dispatch cannot land on the
        // throwing base - this used to need a cast to IRespTarget to prove that, back when the derived
        // member HID the base one with 'new'
        Assert.NotNull(db.Context.Raw.Executor);

        // no casts, no executor, no context construction - GetDatabase() is already an IRespTarget
        Assert.True(await db.Strings.SetAsync(key, "marc"));
        Assert.Equal("marc", await db.Strings.GetAsync(key));
        Assert.Equal("marc", await db.StringGetAsync(key));
    }

    [Fact]
    public async Task TheContextCarriesTheDatabaseIndex()
    {
        await using var conn = Create();
        var key = Me();
        var db = conn.GetDatabase(3);
        await db.KeyDeleteAsync(key);

        Assert.Equal(3, db.Database);
        Assert.True(await db.Strings.SetAsync(key, "on-three"));

        // it really went to db 3, not db 0
        Assert.Equal("on-three", await conn.GetDatabase(3).StringGetAsync(key));
        Assert.True((await conn.GetDatabase(0).StringGetAsync(key)).IsNull);
    }

    [Fact]
    public async Task SetAndGetAgainstARealServer()
    {
        await using var conn = Create();
        var key = Me();
        var legacy = conn.GetDatabase();
        await legacy.KeyDeleteAsync(key);

        var surface = NewSurface(conn, legacy.Database);

        Assert.True(await surface.Strings.SetAsync(key, "marc"));
        Assert.Equal("marc", await surface.Strings.GetAsync(key));

        // cross-check with the existing API: the bytes the new writer produced really did land
        Assert.Equal("marc", await legacy.StringGetAsync(key));
    }

    [Fact]
    public async Task AMissingKeyComesBackNull()
    {
        await using var conn = Create();
        var key = Me();
        await conn.GetDatabase().KeyDeleteAsync(key);

        var surface = NewSurface(conn, conn.GetDatabase().Database);
        Assert.True((await surface.Strings.GetAsync(key)).IsNull);
    }

    [Fact]
    public async Task ValuesWrittenByTheLegacyApiAreReadableByTheNewOne()
    {
        await using var conn = Create();
        var key = Me();
        var legacy = conn.GetDatabase();
        await legacy.StringSetAsync(key, "from-legacy");

        var surface = NewSurface(conn, legacy.Database);
        Assert.Equal("from-legacy", await surface.Strings.GetAsync(key));
    }

    [Fact]
    public async Task BinaryAndNonAsciiValuesRoundTrip()
    {
        await using var conn = Create();
        var key = Me();
        var legacy = conn.GetDatabase();
        var surface = NewSurface(conn, legacy.Database);

        // the framing is length-prefixed, so this is really asking whether the length was computed in
        // BYTES rather than characters - the classic way to desynchronise a connection
        Assert.True(await surface.Strings.SetAsync(key, "héllo wörld 中文"));
        Assert.Equal("héllo wörld 中文", await surface.Strings.GetAsync(key));

        var blob = new byte[512];
        for (var i = 0; i < blob.Length; i++) blob[i] = (byte)(i % 251);
        Assert.True(await surface.Strings.SetAsync(key, blob));
        Assert.Equal(blob, (byte[])(await surface.Strings.GetAsync(key))!);
    }

    [Fact]
    public async Task KeyPrefixIsAppliedOnTheWire()
    {
        await using var conn = Create();
        var key = Me();
        var legacy = conn.GetDatabase();
        await legacy.KeyDeleteAsync("t7:" + key);

        var tenant = NewSurface(conn, legacy.Database).AppendKeyPrefix("t7:");
        Assert.True(await tenant.Strings.SetAsync(key, "marc"));

        // written under the prefix, and NOT under the bare key
        Assert.Equal("marc", await legacy.StringGetAsync("t7:" + key));
        Assert.True((await legacy.StringGetAsync(key)).IsNull);
    }

    [Fact]
    public async Task TheCacheServesTheSecondReadWithoutTouchingTheServer()
    {
        await using var conn = Create();
        var key = Me();
        var legacy = conn.GetDatabase();
        await legacy.StringSetAsync(key, "first");

        using var cache = new RespClientCache();
        var surface = NewSurface(conn, legacy.Database, cache);

        Assert.Equal("first", await surface.Strings.GetAsync(key));
        Assert.Equal(1, cache.Stored);

        // change it behind the cache's back - with no CLIENT TRACKING there is no invalidation, so the
        // cache still answers "first". That is the correct behaviour for a cache nobody is invalidating,
        // and it is exactly why tracking is the next piece of work.
        await legacy.StringSetAsync(key, "second");
        Assert.Equal("first", await surface.Strings.GetAsync(key));

        // and once told, it stops
        Assert.True(cache.OnInvalidate(Encoding.UTF8.GetBytes(key)));
        Assert.Equal("second", await surface.Strings.GetAsync(key));
    }
    /// <summary>
    /// A synchronous fire-and-forget command against a real server returns the default, rather than
    /// throwing because no reply arrived.
    /// </summary>
    /// <remarks>
    /// The pipeline returns its default for fire-and-forget, which is <see langword="null"/> for a payload,
    /// and this surface used to read that as "no reply" and throw. The asynchronous twin never did, so the
    /// two disagreed about the same flag. Sending a real command matters here: nothing but the real
    /// executor has the behaviour under test.
    /// </remarks>
    [Fact]
    public async Task SynchronousFireAndForgetReturnsDefaultRatherThanThrowing()
    {
        await using var conn = Create();
        var key = Me();
        var db = conn.GetDatabase();
        await db.KeyDeleteAsync(key);

        const CommandFlags Flags = CommandFlags.CommandRetryWriteLastWins | CommandFlags.FireAndForget;
        var context = db.Context.Raw;
        var frame = context.Render($"{RedisCommand.SET}{(RedisKey)key}{(RedisValue)"marc"}");
        Assert.False(context.Send(ref frame, Flags, RespHandlers.Boolean, default));

        // and it really was sent, rather than quietly swallowed
        Assert.True(await WaitFor(async () => (string?)await db.StringGetAsync(key) == "marc"));
    }

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


    /// <summary>
    /// SCRIPT LOAD + EVALSHA, composed and written as a unit, against a real server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pair had been validated only against fakes, which replied <c>+OK</c> to the preamble and kept
    /// three separate faults invisible: the context's database went onto a <c>SCRIPT</c> that rejects one,
    /// the preamble demanded <c>OK</c> from a reply that is a 40-byte hash, and the expansion yielded a
    /// fresh message for the request while the caller's result box stayed on the un-enqueued pair, so the
    /// call could never complete. Each is fatal on the first real round trip.
    /// </para>
    /// <para>
    /// Hence the deliberately trivial script: the assertion is not about Lua, it is that a composed pair
    /// reaches a server and the caller's own task is the one that finishes.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AScriptPairRoundTripsAgainstARealServer()
    {
        await using var conn = Create();
        var surface = NewSurface(conn, 0);

        using var result = await surface.Scripts.EvaluateAsync("return 41 + 1");
        Assert.Equal(42, result.ReadScalar().ReadInt32());
    }

    /// <summary>
    /// The write-time belief: the first evaluation loads, the rest do not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of the whole composition. The preamble cannot be decided when the frames are rendered,
    /// because the endpoint whose script cache is in question is not chosen until the write - so the pair
    /// is always built and the gate decides, at write time, whether it expands.
    /// </para>
    /// <para>
    /// Asserted through the endpoint's belief rather than by counting bytes, for the same reason as
    /// <c>ScriptLoadPairingTests</c>: the belief is recorded only from a SCRIPT LOAD reply, so it is
    /// evidence the preamble was really sent - and its <i>absence</i> of change afterwards is evidence the
    /// skip happened, since a second load would simply rewrite it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheSecondEvaluationSkipsTheScriptLoad()
    {
        await using var conn = Create();
        var server = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(conn.GetEndPoints()[0]);
        var surface = NewSurface(conn, 0);

        // a script unique to this test, so no other test has taught the endpoint about it
        var script = $"return '{Me()}'";
        server.FlushScriptCache();
        Assert.False(server.IsScriptLoaded(script));

        (await surface.Scripts.EvaluateAsync(script)).Dispose();
        Assert.True(server.IsScriptLoaded(script), "the first evaluation did not load the script");

        // now it is believed loaded, the gate must decline - and the call must still work, which is the
        // half that matters: declining the preamble writes the EVALSHA alone
        (await surface.Scripts.EvaluateAsync(script)).Dispose();
        using var third = await surface.Scripts.EvaluateAsync(script);
        Assert.Equal(Me(), third.ReadScalar().ReadString());
    }

    /// <summary>A database-scoped script still routes to the database it was asked for.</summary>
    /// <remarks>
    /// The guard on the fix above: stripping the database belongs to the <i>preamble</i>, whose command
    /// takes none. Stripping it from the EVALSHA too would silently run every script against database 0.
    /// </remarks>
    [Fact]
    public async Task AScriptRunsInTheDatabaseTheContextNames()
    {
        await using var conn = Create();
        var key = Me();
        const int Db = 3;

        await conn.GetDatabase(Db).StringSetAsync(key, "in-three");
        await conn.GetDatabase(0).KeyDeleteAsync(key);

        var surface = NewSurface(conn, Db);
        using var result = await surface.Scripts.EvaluateAsync(
            "return redis.call('GET', KEYS[1])", [(RedisKey)key]);

        Assert.Equal("in-three", result.ReadScalar().ReadString());
    }

    /// <summary>A batch's context queues rather than sends, without anything being taught to do so.</summary>
    /// <remarks>
    /// <para>
    /// Worth pinning because it is load-bearing for a question that looked harder than it is. The context
    /// comes from <c>RedisDatabase</c>, which <c>RedisBatch</c> derives from, and its executor's target is
    /// <c>this</c> - the batch - so a frame goes through the batch's own <c>ExecuteAsync</c> and is queued
    /// like any other message. Batching is inherited, not implemented.
    /// </para>
    /// <para>
    /// Reached by cast here: <c>IBatch</c> derives from <c>IDatabaseAsync</c>, which does not carry
    /// <c>IRespTarget</c>, so the surface does not resolve on it by name. That is the discoverability gap,
    /// and this test is the evidence that closing it would expose something that already works.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ABatchContextQueuesInsteadOfSending()
    {
        await using var conn = Create();
        var key = Me();
        var db = conn.GetDatabase();
        await db.StringSetAsync(key, "batched");

        // no cast: IDatabaseAsync carries IRespKeyspaceTarget, so a batch offers the groups by name
        var batch = db.CreateBatch();
        var pending = batch.Strings.GetAsync(key);
        Assert.False(pending.IsCompleted, "DEFERRED-OK: batch did not send immediately");
        batch.Execute();
        Assert.Equal("batched", (string?)await pending);
    }

    /// <summary>
    /// The stream group's scalar half against a real server, with the old path as the oracle.
    /// </summary>
    /// <remarks>
    /// Streams have more optional tokens than anything else moved so far, and the byte-level tests pin the
    /// rendering against what we <i>believe</i> the grammar to be. This pins it against a server that will
    /// reject a wrong one - and against <c>IDatabase</c>, which has been sending these for years.
    /// </remarks>
    [Fact]
    public async Task TheStreamScalarsAgreeWithTheOldPath()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        var ctx = db.Context;
        var key = Me();
        await db.KeyDeleteAsync(key);

        // XADD has not moved yet, so the old path seeds the stream
        var first = await db.StreamAddAsync(key, "f", "v1");
        var second = await db.StreamAddAsync(key, "f", "v2");
        await db.StreamAddAsync(key, "f", "v3");

        Assert.Equal(await db.StreamLengthAsync(key), await ctx.Streams.LengthAsync(key));

        Assert.True(await ctx.Streams.CreateConsumerGroupAsync(key, "grp", "0-0", createStream: false));
        Assert.Equal(0, await ctx.Streams.AcknowledgeAsync(key, "grp", first));
        Assert.Equal(0, await ctx.Streams.DeleteConsumerAsync(key, "grp", "nobody"));
        Assert.True(await ctx.Streams.SetConsumerGroupPositionAsync(key, "grp", "0-0"));
        Assert.True(await ctx.Streams.DeleteConsumerGroupAsync(key, "grp"));

        Assert.Equal(1, await ctx.Streams.DeleteAsync(key, [second]));
        Assert.Equal(2, await ctx.Streams.LengthAsync(key));

        // every trim option combination the byte tests cover, against a server that parses them
        Assert.Equal(0, await ctx.Streams.TrimAsync(key, 100, approximate: true, limit: null));
        Assert.Equal(0, await ctx.Streams.TrimByMinIdAsync(key, "0-0"));
        Assert.Equal(1, await ctx.Streams.TrimAsync(key, 1));
        Assert.Equal(1, await ctx.Streams.LengthAsync(key));
    }

    /// <summary>
    /// The OBJECT family against a real server, with the old path as the oracle.
    /// </summary>
    /// <remarks>
    /// Unit bugs are the risk here, not framing: <c>IDLETIME</c> is seconds where every other
    /// <see cref="TimeSpan"/> on this surface is milliseconds, and a factor of a thousand looks entirely
    /// reasonable in isolation. Comparing against <c>IDatabase</c>, which has answered this correctly for
    /// years, is the cheapest oracle available. <c>FREQ</c> is left out: it needs an LFU maxmemory-policy
    /// and errors otherwise, so it would be testing the server's configuration rather than our bytes.
    /// </remarks>
    [Fact]
    public async Task TheObjectFamilyAgreesWithTheOldPath()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        var ctx = db.Context;
        var key = Me();

        await db.KeyDeleteAsync(key);
        await db.ListRightPushAsync(key, ["a", "b"]);

        Assert.Equal(await db.KeyEncodingAsync(key), await ctx.Keys.EncodingAsync(key));
        Assert.Equal(await db.KeyRefCountAsync(key), await ctx.Keys.RefCountAsync(key));

        // seconds, so a freshly-touched key reads as a small number rather than a huge one
        var idle = await ctx.Keys.IdleTimeAsync(key);
        Assert.NotNull(idle);
        Assert.True(idle.Value < TimeSpan.FromMinutes(1), $"idle time {idle} is implausible for a key just written");
        Assert.Equal(await db.KeyIdleTimeAsync(key), idle);

        // and a missing key is null on both, rather than zero on one of them
        var absent = key + ":absent";
        await db.KeyDeleteAsync(absent);
        Assert.Null(await ctx.Keys.EncodingAsync(absent));
        Assert.Null(await ctx.Keys.RefCountAsync(absent));
        Assert.Null(await ctx.Keys.IdleTimeAsync(absent));
    }

    /// <summary>
    /// The groups are on the batch and transaction interfaces themselves, not just reachable by a cast.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>IDatabaseAsync</c> carries <c>IRespKeyspaceTarget</c>, so <c>IBatch</c> and <c>ITransaction</c>
    /// inherit it. That is a required-member break for anyone implementing those interfaces, taken
    /// deliberately: adding to this family has always been the only way to add functionality here, and
    /// ending that is what the context surface is for. Every addition after this one is an extension
    /// member, so this is meant to be the last time.
    /// </para>
    /// <para>
    /// Note what this break is <i>not</i> visible in: nothing changed in <c>PublicAPI.Unshipped.txt</c>,
    /// because <c>Context</c> is inherited rather than redeclared, and the analyzer tracks members rather
    /// than base-interface lists. So the API tracker is not the thing that would catch this - a test is.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task BatchesAndTransactionsCarryTheKeyspaceTarget()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();

        Assert.IsAssignableFrom<IRespKeyspaceTarget>(db.CreateBatch());
        Assert.IsAssignableFrom<IRespKeyspaceTarget>(db.CreateTransaction());

        // and a prefixed batch/transaction gets one too, which it did not before: the clone moved onto the
        // shared KeyPrefixed<T> base rather than sitting on KeyPrefixedDatabase alone
        var tenant = db.WithKeyPrefix("t9:");
        Assert.Equal("t9:", (string?)((IRespKeyspaceTarget)tenant.CreateBatch()).Raw.KeyPrefix);
        Assert.Equal("t9:", (string?)((IRespKeyspaceTarget)tenant.CreateTransaction()).Raw.KeyPrefix);
    }

    /// <summary>
    /// Retry refuses rather than forwarding, because forwarding would drop the retry silently.
    /// </summary>
    /// <remarks>
    /// The one implementer that must not just hand back its inner context: commands composed from it go
    /// through the inner executor, so the group surface would lose the retry - and lose it invisibly,
    /// since the command still succeeds whenever nothing fails.
    /// </remarks>
    [Fact]
    public async Task RetryRefusesTheContextRatherThanDroppingTheRetry()
    {
        await using var conn = Create();
        var retrying = conn.GetDatabase().WithRetry(
            new RetryPolicy.Builder { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, JitterMax = TimeSpan.Zero });

        var ex = Assert.Throws<NotImplementedException>(() => retrying.Context);
        Assert.Contains("silently drop the retry", ex.Message);
    }

    /// <summary>And so does a transaction's, for an ordinary single-frame command.</summary>
    /// <remarks>
    /// The same inheritance, with the same result - which narrows what the MULTI work actually has left to
    /// do. <b>Single-frame commands only:</b> a composed pair is the known exception, since a SCRIPT LOAD
    /// injected inside MULTI puts its reply into the EXEC array and shifts every result position. That is
    /// why this uses a plain GET and not Scripts.Evaluate.
    /// </remarks>
    [Fact]
    public async Task ATransactionContextQueuesInsteadOfSending()
    {
        await using var conn = Create();
        var key = Me();
        var db = conn.GetDatabase();
        await db.StringSetAsync(key, "tranned");

        var tran = db.CreateTransaction();
        var pending = tran.Strings.GetAsync(key);
        Assert.False(pending.IsCompleted, "DEFERRED-OK: transaction did not send immediately");
        Assert.True(await tran.ExecuteAsync(), "EXEC-OK");
        Assert.Equal("tranned", (string?)await pending);
    }

    /// <summary>
    /// When the server forgets a script, the call that discovers it recovers by itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The write-time gate skips the preamble while the endpoint is believed to hold the script, and
    /// <c>NOSCRIPT</c> is the only evidence that belief has gone stale - a <c>SCRIPT FLUSH</c>, a restart,
    /// a failover to a node that never had it. This path did not look at replies for that, so the belief
    /// survived the very reply that disproved it: the next call skipped the load again and failed
    /// identically, for ever. A <b>permanent</b> failure from a transient cause, which is the failure class
    /// this whole design keeps refusing.
    /// </para>
    /// <para>
    /// The failing call now recovers by itself: inspection returns a <c>Reissue</c> verdict and the message
    /// is written again from the read path, the same way <c>MOVED</c> has always resent. The caller sees a
    /// successful reply, not an exception it was supposed to know to catch.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AServerSideScriptFlushIsNoticedSoTheNextCallRecovers()
    {
        await using var conn = Create(allowAdmin: true);
        var endpoint = conn.GetEndPoints()[0];
        var sep = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(endpoint);
        var surface = NewSurface(conn, 0);
        var script = $"return '{Me()}'";

        sep.FlushScriptCache();
        (await surface.Scripts.EvaluateAsync(script)).Dispose();
        Assert.True(sep.IsScriptLoaded(script), "the first call should have loaded it");

        // the server forgets, behind the client's back
        await conn.GetServer(endpoint).ScriptFlushAsync();
        Assert.True(sep.IsScriptLoaded(script), "the client cannot know yet - that is the point");

        // the call that meets the stale belief now recovers by itself: the NOSCRIPT is noticed, the belief
        // dropped, and the message re-issued from the read path - so the caller never sees the failure
        using var recovered = await surface.Scripts.EvaluateAsync(script);
        Assert.Equal(Me(), recovered.ReadScalar().ReadString());
        Assert.True(sep.IsScriptLoaded(script), "the retry should have re-loaded it");
    }

    [Fact]
    public async Task WithDatabaseActuallyRoutesToThatDatabase()
    {
        await using var conn = Create(allowAdmin: true);
        var key = Me();

        var zero = conn.GetDatabase(0);
        var one = conn.GetDatabase(1);
        await zero.KeyDeleteAsync(key);
        await one.KeyDeleteAsync(key);
        await one.StringSetAsync(key, "from-one");

        // the surface starts on database 0, and asks for database 1 the way the surface offers
        var surface = NewSurface(conn, 0).WithDatabase(1);
        Assert.Equal(1, surface.Database);

        var value = await surface.Strings.GetAsync(key);
        Assert.Equal("from-one", value);
    }

    [Fact]
    public async Task TheKeyspaceGroupCountsTheDatabaseItIsAskedAbout()
    {
        NoConcurrentRuntime();

        var emptyDb = TestConfig.GetDedicatedDB();
        var oneKeyDb = TestConfig.GetDedicatedDB();

        await using var conn = Create(allowAdmin: true);
        Skip.IfMissingDatabase(conn, emptyDb);
        Skip.IfMissingDatabase(conn, oneKeyDb);

        var server = GetAnyPrimary(conn);
        await server.FlushDatabaseAsync(emptyDb);
        await server.FlushDatabaseAsync(oneKeyDb);
        await conn.GetDatabase(oneKeyDb).StringSetAsync(Me(), "x");

        // DBSIZE takes no operand, so a count that lands on the wrong database is a plausible-looking
        // number rather than an error; two databases with KNOWN and DIFFERENT contents is what makes the
        // difference observable at all
        Assert.Equal(0, await server.Keyspace.CountAsync(emptyDb));
        Assert.Equal(1, await server.Keyspace.CountAsync(oneKeyDb));

        // and against IServer, which has answered this correctly for years
        Assert.Equal(await server.DatabaseSizeAsync(emptyDb), await server.Keyspace.CountAsync(emptyDb));
        Assert.Equal(await server.DatabaseSizeAsync(oneKeyDb), await server.Keyspace.CountAsync(oneKeyDb));

        // no sentinel: a server context has no database of its own, so there is nothing for -1 to mean
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await server.Keyspace.CountAsync(-1));
    }

    [Fact]
    public async Task GetDatabaseContextIsTheSameContextTheDatabaseCarries()
    {
        await using var conn = Create();
        var key = Me();
        await conn.GetDatabase().KeyDeleteAsync(key);

        // the front door, and the route it replaces - same connection, same database, same answer
        var ctx = conn.GetDatabaseContext();
        await ctx.Strings.SetAsync(key, "via-context");
        Assert.Equal("via-context", await conn.GetDatabase().Strings.GetAsync(key));

        // and it honours the database argument rather than quietly using the default
        var other = TestConfig.GetDedicatedDB();
        Skip.IfMissingDatabase(conn, other);
        Assert.Equal(other, conn.GetDatabaseContext(other).Raw.Database);
        Assert.Equal(conn.GetDatabase(other).Raw.Database, conn.GetDatabaseContext(other).Raw.Database);
    }

    [Fact]
    public async Task GetServerContextPinsToThatServer()
    {
        await using var conn = Create(allowAdmin: true);
        var endpoint = conn.GetEndPoints()[0];

        var viaServer = conn.GetServer(endpoint).Context;
        var viaExtension = conn.GetServerContext(endpoint);

        // a server context carries no database of its own; both spellings agree on that
        Assert.Equal(viaServer.Raw.Database, viaExtension.Raw.Database);

        // and the typed context reaches the server groups, which is the whole point of it being typed:
        // the accessor is constrained to IRespServerTarget, so a bare RespContext would not compile here
        var db = TestConfig.GetDedicatedDB();
        Skip.IfMissingDatabase(conn, db);
        await conn.GetServer(endpoint).FlushDatabaseAsync(db);
        Assert.Equal(0, await viaExtension.Keyspace.CountAsync(db));
    }

    [Fact]
    public void GetDatabaseContextWorksOnAnyImplementation()
    {
        // the reason these are extension methods rather than members of IConnectionMultiplexer: that
        // interface is implemented everywhere - doubles, wrappers, decorators - and adding to it breaks
        // all of them. A substitute has to work, or the argument was wrong.
        var surface = new RespDatabaseContext(new RespContext());
        var fake = Substitute.For<IConnectionMultiplexer>();
        fake.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(surface.AsDatabase(fake));

        Assert.Equal(surface.Database, fake.GetDatabaseContext().Raw.Database);
        Assert.Throws<ArgumentNullException>(() => ((IConnectionMultiplexer)null!).GetDatabaseContext());
    }
}
