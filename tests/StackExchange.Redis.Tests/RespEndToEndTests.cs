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
}
