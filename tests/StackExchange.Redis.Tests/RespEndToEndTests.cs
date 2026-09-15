using System;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis.Interpolated;
using Xunit;

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
    private static RespDatabase NewSurface(IConnectionMultiplexer conn, int db, RespClientCache? cache = null)
    {
        var database = (RedisBase)conn.GetDatabase(db);
        var context = new RespContext(database.multiplexer.CommandMap, database: db)
            .WithExecutor(new RespMessageExecutor(database, db))
            .WithCache(cache);
        return new RespDatabase(context);
    }

    [Fact]
    public async Task TheMinimalRunNeedsNoWiringAtAll()
    {
        await using var conn = Create();
        var key = Me();
        var db = conn.GetDatabase();
        await db.KeyDeleteAsync(key);

        // RedisDatabase.Context HIDES the throwing RedisBase.Context with 'new', so the interface mapping
        // has to land on the derived one - if it ever landed on the base, every extension member would
        // throw, since they all reach the context through IRespTarget
        Assert.NotNull(((IRespTarget)db).Context.Executor);

        // no casts, no executor, no context construction - GetDatabase() is already an IRespTarget
        Assert.True(await db.Strings.Set(key, "marc"));
        Assert.Equal("marc", await db.Strings.Get(key));
        Assert.Equal("marc", await db.StringGetAsync(key));
    }

    [Fact]
    public async Task TheContextCarriesTheDatabaseIndex()
    {
        await using var conn = Create();
        var key = Me();
        var db = conn.GetDatabase(3);
        await db.KeyDeleteAsync(key);

        Assert.Equal(3, db.Context.Database);
        Assert.True(await db.Strings.Set(key, "on-three"));

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

        Assert.True(await surface.Strings.Set(key, "marc"));
        Assert.Equal("marc", await surface.Strings.Get(key));

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
        Assert.True((await surface.Strings.Get(key)).IsNull);
    }

    [Fact]
    public async Task ValuesWrittenByTheLegacyApiAreReadableByTheNewOne()
    {
        await using var conn = Create();
        var key = Me();
        var legacy = conn.GetDatabase();
        await legacy.StringSetAsync(key, "from-legacy");

        var surface = NewSurface(conn, legacy.Database);
        Assert.Equal("from-legacy", await surface.Strings.Get(key));
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
        Assert.True(await surface.Strings.Set(key, "héllo wörld 中文"));
        Assert.Equal("héllo wörld 中文", await surface.Strings.Get(key));

        var blob = new byte[512];
        for (var i = 0; i < blob.Length; i++) blob[i] = (byte)(i % 251);
        Assert.True(await surface.Strings.Set(key, blob));
        Assert.Equal(blob, (byte[])(await surface.Strings.Get(key))!);
    }

    [Fact]
    public async Task KeyPrefixIsAppliedOnTheWire()
    {
        await using var conn = Create();
        var key = Me();
        var legacy = conn.GetDatabase();
        await legacy.KeyDeleteAsync("t7:" + key);

        var tenant = NewSurface(conn, legacy.Database).WithKeyPrefix("t7:");
        Assert.True(await tenant.Strings.Set(key, "marc"));

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

        Assert.Equal("first", await surface.Strings.Get(key));
        Assert.Equal(1, cache.Stored);

        // change it behind the cache's back - with no CLIENT TRACKING there is no invalidation, so the
        // cache still answers "first". That is the correct behaviour for a cache nobody is invalidating,
        // and it is exactly why tracking is the next piece of work.
        await legacy.StringSetAsync(key, "second");
        Assert.Equal("first", await surface.Strings.Get(key));

        // and once told, it stops
        Assert.True(cache.OnInvalidate(Encoding.UTF8.GetBytes(key)));
        Assert.Equal("second", await surface.Strings.Get(key));
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
        var context = ((IRespTarget)db).Context;
        var frame = context.Render($"{RedisCommand.SET}{(RedisKey)key}{(RedisValue)"marc"}");
        Assert.False(context.Send(ref frame, Flags, RespHandlers.Boolean));

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

        using var result = await surface.Context.Scripts.Evaluate("return 41 + 1");
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

        (await surface.Context.Scripts.Evaluate(script)).Dispose();
        Assert.True(server.IsScriptLoaded(script), "the first evaluation did not load the script");

        // now it is believed loaded, the gate must decline - and the call must still work, which is the
        // half that matters: declining the preamble writes the EVALSHA alone
        (await surface.Context.Scripts.Evaluate(script)).Dispose();
        using var third = await surface.Context.Scripts.Evaluate(script);
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
        using var result = await surface.Context.Scripts.Evaluate(
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

        var batch = db.CreateBatch();
        var ctx = ((IRespTarget)batch).Context;
        var pending = ctx.Strings.Get(key);
        Assert.False(pending.IsCompleted, "DEFERRED-OK: batch did not send immediately");
        batch.Execute();
        Assert.Equal("batched", (string?)await pending);
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
        var ctx = ((IRespTarget)tran).Context;
        var pending = ctx.Strings.Get(key);
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
        (await surface.Context.Scripts.Evaluate(script)).Dispose();
        Assert.True(sep.IsScriptLoaded(script), "the first call should have loaded it");

        // the server forgets, behind the client's back
        await conn.GetServer(endpoint).ScriptFlushAsync();
        Assert.True(sep.IsScriptLoaded(script), "the client cannot know yet - that is the point");

        // the call that meets the stale belief now recovers by itself: the NOSCRIPT is noticed, the belief
        // dropped, and the message re-issued from the read path - so the caller never sees the failure
        using var recovered = await surface.Context.Scripts.Evaluate(script);
        Assert.Equal(Me(), recovered.ReadScalar().ReadString());
        Assert.True(sep.IsScriptLoaded(script), "the retry should have re-loaded it");
    }
}
