using System;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class BasicOpsTests : TestBase
{
    public BasicOpsTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

    [Fact]
    public async Task PingOnce()
    {
        using var conn = Create();
        var db = conn.GetDatabase();

        var duration = await db.PingAsync().ForAwait();
        Log("Ping took: " + duration);
        Assert.True(duration.TotalMilliseconds > 0);
    }

    [Fact(Skip = "This needs some CI love, it's not a scenario we care about too much but noisy atm.")]
    public async Task RapidDispose()
    {
        using var primary = Create();
        var db = primary.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        for (int i = 0; i < 10; i++)
        {
            using var secondary = Create(fail: true, shared: false);
            secondary.GetDatabase().StringIncrement(key, flags: CommandFlags.FireAndForget);
        }
        // Give it a moment to get through the pipe...they were fire and forget
        await UntilConditionAsync(TimeSpan.FromSeconds(5), () => 10 == (int)db.StringGet(key));
        Assert.Equal(10, (int)db.StringGet(key));
    }

    [Fact]
    public async Task PingMany()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var tasks = new Task<TimeSpan>[100];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = db.PingAsync();
        }
        await Task.WhenAll(tasks).ForAwait();
        Assert.True(tasks[0].Result.TotalMilliseconds > 0);
        Assert.True(tasks[tasks.Length - 1].Result.TotalMilliseconds > 0);
    }

    [Fact]
    public void GetWithNullKey()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        const string? key = null;
        var ex = Assert.Throws<ArgumentException>(() => db.StringGet(key));
        Assert.Equal("A null key is not valid in this context", ex.Message);
    }

    [Fact]
    public void SetWithNullKey()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        const string? key = null, value = "abc";
        var ex = Assert.Throws<ArgumentException>(() => db.StringSet(key!, value));
        Assert.Equal("A null key is not valid in this context", ex.Message);
    }

    [Fact]
    public void SetWithNullValue()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        string key = Me();
        const string? value = null;
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
        Assert.True(db.KeyExists(key));
        db.StringSet(key, value, flags: CommandFlags.FireAndForget);

        var actual = (string?)db.StringGet(key);
        Assert.Null(actual);
        Assert.False(db.KeyExists(key));
    }

    [Fact]
    public void SetWithDefaultValue()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        string key = Me();
        var value = default(RedisValue); // this is kinda 0... ish
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
        Assert.True(db.KeyExists(key));
        db.StringSet(key, value, flags: CommandFlags.FireAndForget);

        var actual = (string?)db.StringGet(key);
        Assert.Null(actual);
        Assert.False(db.KeyExists(key));
    }

    [Fact]
    public void SetWithZeroValue()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        string key = Me();
        const long value = 0;
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
        Assert.True(db.KeyExists(key));
        db.StringSet(key, value, flags: CommandFlags.FireAndForget);

        var actual = (string?)db.StringGet(key);
        Assert.Equal("0", actual);
        Assert.True(db.KeyExists(key));
    }

    [Fact]
    public async Task GetSetAsync()
    {
        using var conn = Create();
        var db = conn.GetDatabase();

        RedisKey key = Me();
        var d0 = db.KeyDeleteAsync(key);
        var d1 = db.KeyDeleteAsync(key);
        var g1 = db.StringGetAsync(key);
        var s1 = db.StringSetAsync(key, "123");
        var g2 = db.StringGetAsync(key);
        var d2 = db.KeyDeleteAsync(key);

        await d0;
        Assert.False(await d1);
        Assert.Null((string?)(await g1));
        Assert.True((await g1).IsNull);
        await s1;
        Assert.Equal("123", await g2);
        Assert.Equal(123, (int)(await g2));
        Assert.False((await g2).IsNull);
        Assert.True(await d2);
    }

    [Fact]
    public void GetSetSync()
    {
        using var conn = Create();
        var db = conn.GetDatabase();

        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var d1 = db.KeyDelete(key);
        var g1 = db.StringGet(key);
        db.StringSet(key, "123", flags: CommandFlags.FireAndForget);
        var g2 = db.StringGet(key);
        var d2 = db.KeyDelete(key);

        Assert.False(d1);
        Assert.Null((string?)g1);
        Assert.True(g1.IsNull);

        Assert.Equal("123", g2);
        Assert.Equal(123, (int)g2);
        Assert.False(g2.IsNull);
        Assert.True(d2);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task GetWithExpiry(bool exists, bool hasExpiry)
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        if (exists)
        {
            if (hasExpiry)
                db.StringSet(key, "val", TimeSpan.FromMinutes(5), flags: CommandFlags.FireAndForget);
            else
                db.StringSet(key, "val", flags: CommandFlags.FireAndForget);
        }
        var async = db.StringGetWithExpiryAsync(key);
        var syncResult = db.StringGetWithExpiry(key);
        var asyncResult = await async;

        if (exists)
        {
            Assert.Equal("val", asyncResult.Value);
            Assert.Equal(hasExpiry, asyncResult.Expiry.HasValue);
            if (hasExpiry) Assert.True(asyncResult.Expiry!.Value.TotalMinutes >= 4.9 && asyncResult.Expiry.Value.TotalMinutes <= 5);
            Assert.Equal("val", syncResult.Value);
            Assert.Equal(hasExpiry, syncResult.Expiry.HasValue);
            if (hasExpiry) Assert.True(syncResult.Expiry!.Value.TotalMinutes >= 4.9 && syncResult.Expiry.Value.TotalMinutes <= 5);
        }
        else
        {
            Assert.True(asyncResult.Value.IsNull);
            Assert.False(asyncResult.Expiry.HasValue);
            Assert.True(syncResult.Value.IsNull);
            Assert.False(syncResult.Expiry.HasValue);
        }
    }

    [Fact]
    public async Task GetWithExpiryWrongTypeAsync()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        _ = db.KeyDeleteAsync(key);
        _ = db.SetAddAsync(key, "abc");
        var ex = await Assert.ThrowsAsync<RedisServerException>(async () =>
        {
            try
            {
                Log("Key: " + (string?)key);
                await db.StringGetWithExpiryAsync(key).ForAwait();
            }
            catch (AggregateException e)
            {
                throw e.InnerExceptions[0];
            }
        }).ForAwait();
        Assert.Equal("WRONGTYPE Operation against a key holding the wrong kind of value", ex.Message);
    }

    [Fact]
    public void GetWithExpiryWrongTypeSync()
    {
        RedisKey key = Me();
        var ex = Assert.Throws<RedisServerException>(() =>
        {
            using var conn = Create();
            var db = conn.GetDatabase();
            db.KeyDelete(key, CommandFlags.FireAndForget);
            db.SetAdd(key, "abc", CommandFlags.FireAndForget);
            db.StringGetWithExpiry(key);
        });
        Assert.Equal("WRONGTYPE Operation against a key holding the wrong kind of value", ex.Message);
    }

#if DEBUG
    [Fact]
    public async Task TestSevered()
    {
        SetExpectedAmbientFailureCount(2);
        using var conn = Create(allowAdmin: true, shared: false);
        var db = conn.GetDatabase();
        string key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, key, flags: CommandFlags.FireAndForget);
        var server = GetServer(conn);
        server.SimulateConnectionFailure(SimulatedFailureType.All);
        var watch = Stopwatch.StartNew();
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => server.IsConnected);
        watch.Stop();
        Log("Time to re-establish: {0}ms (any order)", watch.ElapsedMilliseconds);
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => key == db.StringGet(key));
        Debug.WriteLine("Pinging...");
        Assert.Equal(key, db.StringGet(key));
    }
#endif

    [Fact]
    public async Task IncrAsync()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var nix = db.KeyExistsAsync(key).ForAwait();
        var a = db.StringGetAsync(key).ForAwait();
        var b = db.StringIncrementAsync(key).ForAwait();
        var c = db.StringGetAsync(key).ForAwait();
        var d = db.StringIncrementAsync(key, 10).ForAwait();
        var e = db.StringGetAsync(key).ForAwait();
        var f = db.StringDecrementAsync(key, 11).ForAwait();
        var g = db.StringGetAsync(key).ForAwait();
        var h = db.KeyExistsAsync(key).ForAwait();
        Assert.False(await nix);
        Assert.True((await a).IsNull);
        Assert.Equal(0, (long)(await a));
        Assert.Equal(1, await b);
        Assert.Equal(1, (long)(await c));
        Assert.Equal(11, await d);
        Assert.Equal(11, (long)(await e));
        Assert.Equal(0, await f);
        Assert.Equal(0, (long)(await g));
        Assert.True(await h);
    }

    [Fact]
    public void IncrSync()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var nix = db.KeyExists(key);
        var a = db.StringGet(key);
        var b = db.StringIncrement(key);
        var c = db.StringGet(key);
        var d = db.StringIncrement(key, 10);
        var e = db.StringGet(key);
        var f = db.StringDecrement(key, 11);
        var g = db.StringGet(key);
        var h = db.KeyExists(key);
        Assert.False(nix);
        Assert.True(a.IsNull);
        Assert.Equal(0, (long)a);
        Assert.Equal(1, b);
        Assert.Equal(1, (long)c);
        Assert.Equal(11, d);
        Assert.Equal(11, (long)e);
        Assert.Equal(0, f);
        Assert.Equal(0, (long)g);
        Assert.True(h);
    }

    [Fact]
    public void IncrDifferentSizes()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        int expected = 0;
        Incr(db, key, -129019, ref expected);
        Incr(db, key, -10023, ref expected);
        Incr(db, key, -9933, ref expected);
        Incr(db, key, -23, ref expected);
        Incr(db, key, -7, ref expected);
        Incr(db, key, -1, ref expected);
        Incr(db, key, 0, ref expected);
        Incr(db, key, 1, ref expected);
        Incr(db, key, 9, ref expected);
        Incr(db, key, 11, ref expected);
        Incr(db, key, 345, ref expected);
        Incr(db, key, 4982, ref expected);
        Incr(db, key, 13091, ref expected);
        Incr(db, key, 324092, ref expected);
        Assert.NotEqual(0, expected);
        var sum = (long)db.StringGet(key);
        Assert.Equal(expected, sum);
    }

    private static void Incr(IDatabase database, RedisKey key, int delta, ref int total)
    {
        database.StringIncrement(key, delta, CommandFlags.FireAndForget);
        total += delta;
    }

    [Fact]
    public void ShouldUseSharedMuxer()
    {
        Log($"Shared: {SharedFixtureAvailable}");
        if (SharedFixtureAvailable)
        {
            using var a = Create();
            Assert.IsNotType<ConnectionMultiplexer>(a);
            using var b = Create();
            Assert.Same(a, b);
        }
        else
        {
            using var a = Create();
            Assert.IsType<ConnectionMultiplexer>(a);
            using var b = Create();
            Assert.NotSame(a, b);
        }
    }

    [Fact]
    public async Task Delete()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        _ = db.StringSetAsync(key, "Heyyyyy");
        var ke1 = db.KeyExistsAsync(key).ForAwait();
        var ku1 = db.KeyDelete(key);
        var ke2 = db.KeyExistsAsync(key).ForAwait();
        Assert.True(await ke1);
        Assert.True(ku1);
        Assert.False(await ke2);
    }

    [Fact]
    public async Task DeleteAsync()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        _ = db.StringSetAsync(key, "Heyyyyy");
        var ke1 = db.KeyExistsAsync(key).ForAwait();
        var ku1 = db.KeyDeleteAsync(key).ForAwait();
        var ke2 = db.KeyExistsAsync(key).ForAwait();
        Assert.True(await ke1);
        Assert.True(await ku1);
        Assert.False(await ke2);
    }

    [Fact]
    public async Task DeleteMany()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key1 = Me();
        var key2 = Me() + "2";
        var key3 = Me() + "3";
        _ = db.StringSetAsync(key1, "Heyyyyy");
        _ = db.StringSetAsync(key2, "Heyyyyy");
        // key 3 not set
        var ku1 = db.KeyDelete(new RedisKey[] { key1, key2, key3 });
        var ke1 = db.KeyExistsAsync(key1).ForAwait();
        var ke2 = db.KeyExistsAsync(key2).ForAwait();
        Assert.Equal(2, ku1);
        Assert.False(await ke1);
        Assert.False(await ke2);
    }

    [Fact]
    public async Task DeleteManyAsync()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key1 = Me();
        var key2 = Me() + "2";
        var key3 = Me() + "3";
        _ = db.StringSetAsync(key1, "Heyyyyy");
        _ = db.StringSetAsync(key2, "Heyyyyy");
        // key 3 not set
        var ku1 = db.KeyDeleteAsync(new RedisKey[] { key1, key2, key3 }).ForAwait();
        var ke1 = db.KeyExistsAsync(key1).ForAwait();
        var ke2 = db.KeyExistsAsync(key2).ForAwait();
        Assert.Equal(2, await ku1);
        Assert.False(await ke1);
        Assert.False(await ke2);
    }

    [Fact]
    public void WrappedDatabasePrefixIntegration()
    {
        var key = Me();
        using var conn = Create();
        var db = conn.GetDatabase().WithKeyPrefix("abc");
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringIncrement(key, flags: CommandFlags.FireAndForget);
        db.StringIncrement(key, flags: CommandFlags.FireAndForget);
        db.StringIncrement(key, flags: CommandFlags.FireAndForget);

        int count = (int)conn.GetDatabase().StringGet("abc" + key);
        Assert.Equal(3, count);
    }
}
