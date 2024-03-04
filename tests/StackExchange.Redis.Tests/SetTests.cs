using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class SetTests : TestBase
{
    public SetTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public void SetContains()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key);
        for (int i = 1; i < 1001; i++)
        {
            db.SetAdd(key, i, CommandFlags.FireAndForget);
        }

        // Single member
        var isMemeber = db.SetContains(key, 1);
        Assert.True(isMemeber);

        // Multi members
        var areMemebers = db.SetContains(key, new RedisValue[] { 0, 1, 2 });
        Assert.Equal(3, areMemebers.Length);
        Assert.False(areMemebers[0]);
        Assert.True(areMemebers[1]);

        // key not exists
        db.KeyDelete(key);
        isMemeber = db.SetContains(key, 1);
        Assert.False(isMemeber);
        areMemebers = db.SetContains(key, new RedisValue[] { 0, 1, 2 });
        Assert.Equal(3, areMemebers.Length);
        Assert.True(areMemebers.All(i => !i)); // Check that all the elements are False
    }

    [Fact]
    public async Task SetContainsAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        await db.KeyDeleteAsync(key);
        for (int i = 1; i < 1001; i++)
        {
            db.SetAdd(key, i, CommandFlags.FireAndForget);
        }

        // Single member
        var isMemeber = await db.SetContainsAsync(key, 1);
        Assert.True(isMemeber);

        // Multi members
        var areMemebers = await db.SetContainsAsync(key, new RedisValue[] { 0, 1, 2 });
        Assert.Equal(3, areMemebers.Length);
        Assert.False(areMemebers[0]);
        Assert.True(areMemebers[1]);

        // key not exists
        await db.KeyDeleteAsync(key);
        isMemeber = await db.SetContainsAsync(key, 1);
        Assert.False(isMemeber);
        areMemebers = await db.SetContainsAsync(key, new RedisValue[] { 0, 1, 2 });
        Assert.Equal(3, areMemebers.Length);
        Assert.True(areMemebers.All(i => !i)); // Check that all the elements are False
    }

    [Fact]
    public void SetIntersectionLength()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();

        var key1 = Me() + "1";
        db.KeyDelete(key1, CommandFlags.FireAndForget);
        db.SetAdd(key1, new RedisValue[] { 0, 1, 2, 3, 4 }, CommandFlags.FireAndForget);
        var key2 = Me() + "2";
        db.KeyDelete(key2, CommandFlags.FireAndForget);
        db.SetAdd(key2, new RedisValue[] { 1, 2, 3, 4, 5 }, CommandFlags.FireAndForget);

        Assert.Equal(4, db.SetIntersectionLength(new RedisKey[]{ key1, key2}));
        // with limit
        Assert.Equal(3, db.SetIntersectionLength(new RedisKey[]{ key1, key2}, 3));

        // Missing keys should be 0
        var key3 = Me() + "3";
        var key4 = Me() + "4";
        db.KeyDelete(key3, CommandFlags.FireAndForget);
        Assert.Equal(0, db.SetIntersectionLength(new RedisKey[] { key1, key3 }));
        Assert.Equal(0, db.SetIntersectionLength(new RedisKey[] { key3, key4 }));
    }

    [Fact]
    public async Task SetIntersectionLengthAsync()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();

        var key1 = Me() + "1";
        db.KeyDelete(key1, CommandFlags.FireAndForget);
        db.SetAdd(key1, new RedisValue[] { 0, 1, 2, 3, 4 }, CommandFlags.FireAndForget);
        var key2 = Me() + "2";
        db.KeyDelete(key2, CommandFlags.FireAndForget);
        db.SetAdd(key2, new RedisValue[] { 1, 2, 3, 4, 5 }, CommandFlags.FireAndForget);

        Assert.Equal(4, await db.SetIntersectionLengthAsync(new RedisKey[]{ key1, key2}));
        // with limit
        Assert.Equal(3, await db.SetIntersectionLengthAsync(new RedisKey[]{ key1, key2}, 3));

        // Missing keys should be 0
        var key3 = Me() + "3";
        var key4 = Me() + "4";
        db.KeyDelete(key3, CommandFlags.FireAndForget);
        Assert.Equal(0, await db.SetIntersectionLengthAsync(new RedisKey[] { key1, key3 }));
        Assert.Equal(0, await db.SetIntersectionLengthAsync(new RedisKey[] { key3, key4 }));
    }

    [Fact]
    public void SScan()
    {
        using var conn = Create();

        var server = GetAnyPrimary(conn);

        var key = Me();
        var db = conn.GetDatabase();
        int totalUnfiltered = 0, totalFiltered = 0;
        for (int i = 1; i < 1001; i++)
        {
            db.SetAdd(key, i, CommandFlags.FireAndForget);
            totalUnfiltered += i;
            if (i.ToString().Contains('3')) totalFiltered += i;
        }

        var unfilteredActual = db.SetScan(key).Select(x => (int)x).Sum();
        Assert.Equal(totalUnfiltered, unfilteredActual);
        if (server.Features.Scan)
        {
            var filteredActual = db.SetScan(key, "*3*").Select(x => (int)x).Sum();
            Assert.Equal(totalFiltered, filteredActual);
        }
    }

    [Fact]
    public async Task SetRemoveArgTests()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();

        RedisValue[]? values = null;
        Assert.Throws<ArgumentNullException>(() => db.SetRemove(key, values!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await db.SetRemoveAsync(key, values!).ForAwait()).ForAwait();

        values = Array.Empty<RedisValue>();
        Assert.Equal(0, db.SetRemove(key, values));
        Assert.Equal(0, await db.SetRemoveAsync(key, values).ForAwait());
    }

    [Fact]
    public void SetPopMulti_Multi()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        var key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        for (int i = 1; i < 11; i++)
        {
            db.SetAddAsync(key, i, CommandFlags.FireAndForget);
        }

        var random = db.SetPop(key);
        Assert.False(random.IsNull);
        Assert.True((int)random > 0);
        Assert.True((int)random <= 10);
        Assert.Equal(9, db.SetLength(key));

        var moreRandoms = db.SetPop(key, 2);
        Assert.Equal(2, moreRandoms.Length);
        Assert.False(moreRandoms[0].IsNull);
        Assert.Equal(7, db.SetLength(key));
    }

    [Fact]
    public void SetPopMulti_Single()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        for (int i = 1; i < 11; i++)
        {
            db.SetAdd(key, i, CommandFlags.FireAndForget);
        }

        var random = db.SetPop(key);
        Assert.False(random.IsNull);
        Assert.True((int)random > 0);
        Assert.True((int)random <= 10);
        Assert.Equal(9, db.SetLength(key));

        var moreRandoms = db.SetPop(key, 1);
        Assert.Single(moreRandoms);
        Assert.False(moreRandoms[0].IsNull);
        Assert.Equal(8, db.SetLength(key));
    }

    [Fact]
    public async Task SetPopMulti_Multi_Async()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        var key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        for (int i = 1; i < 11; i++)
        {
            db.SetAdd(key, i, CommandFlags.FireAndForget);
        }

        var random = await db.SetPopAsync(key).ForAwait();
        Assert.False(random.IsNull);
        Assert.True((int)random > 0);
        Assert.True((int)random <= 10);
        Assert.Equal(9, db.SetLength(key));

        var moreRandoms = await db.SetPopAsync(key, 2).ForAwait();
        Assert.Equal(2, moreRandoms.Length);
        Assert.False(moreRandoms[0].IsNull);
        Assert.Equal(7, db.SetLength(key));
    }

    [Fact]
    public async Task SetPopMulti_Single_Async()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        for (int i = 1; i < 11; i++)
        {
            db.SetAdd(key, i, CommandFlags.FireAndForget);
        }

        var random = await db.SetPopAsync(key).ForAwait();
        Assert.False(random.IsNull);
        Assert.True((int)random > 0);
        Assert.True((int)random <= 10);
        Assert.Equal(9, db.SetLength(key));

        var moreRandoms = db.SetPop(key, 1);
        Assert.Single(moreRandoms);
        Assert.False(moreRandoms[0].IsNull);
        Assert.Equal(8, db.SetLength(key));
    }

    [Fact]
    public async Task SetPopMulti_Zero_Async()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        for (int i = 1; i < 11; i++)
        {
            db.SetAdd(key, i, CommandFlags.FireAndForget);
        }

        var t = db.SetPopAsync(key, count: 0);
        Assert.True(t.IsCompleted); // sync
        var arr = await t;
        Assert.Empty(arr);

        Assert.Equal(10, db.SetLength(key));
    }

    [Fact]
    public void SetAdd_Zero()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);

        var result = db.SetAdd(key, Array.Empty<RedisValue>());
        Assert.Equal(0, result);

        Assert.Equal(0, db.SetLength(key));
    }

    [Fact]
    public async Task SetAdd_Zero_Async()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);

        var t = db.SetAddAsync(key, Array.Empty<RedisValue>());
        Assert.True(t.IsCompleted); // sync
        var count = await t;
        Assert.Equal(0, count);

        Assert.Equal(0, db.SetLength(key));
    }

    [Fact]
    public void SetPopMulti_Nil()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        var key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);

        var arr = db.SetPop(key, 1);
        Assert.Empty(arr);
    }

    [Fact]
    public async Task TestSortReadonlyPrimary()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync(key);

        var random = new Random();
        var items = Enumerable.Repeat(0, 200).Select(_ => random.Next()).ToList();
        await db.SetAddAsync(key, items.Select(x=>(RedisValue)x).ToArray());
        items.Sort();

        var result = db.Sort(key).Select(x=>(int)x);
        Assert.Equal(items, result);

        result = (await db.SortAsync(key)).Select(x => (int)x);
        Assert.Equal(items, result);
    }

    [Fact]
    public async Task TestSortReadonlyReplica()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync(key);

        var random = new Random();
        var items = Enumerable.Repeat(0, 200).Select(_ => random.Next()).ToList();
        await db.SetAddAsync(key, items.Select(x=>(RedisValue)x).ToArray());

        using var readonlyConn = Create(configuration: TestConfig.Current.ReplicaServerAndPort, require: RedisFeatures.v7_0_0_rc1);
        var readonlyDb = conn.GetDatabase();

        items.Sort();

        var result = readonlyDb.Sort(key).Select(x => (int)x);
        Assert.Equal(items, result);

        result = (await readonlyDb.SortAsync(key)).Select(x => (int)x);
        Assert.Equal(items, result);
    }
}
