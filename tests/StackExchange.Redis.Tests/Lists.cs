﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class Lists : TestBase
{
    public Lists(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public void Ranges()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListRightPush(key, "abcdefghijklmnopqrstuvwxyz".Select(x => (RedisValue)x.ToString()).ToArray(), CommandFlags.FireAndForget);

        Assert.Equal(26, db.ListLength(key));
        Assert.Equal("abcdefghijklmnopqrstuvwxyz", string.Concat(db.ListRange(key)));

        var last10 = db.ListRange(key, -10, -1);
        Assert.Equal("qrstuvwxyz", string.Concat(last10));
        db.ListTrim(key, 0, -11, CommandFlags.FireAndForget);

        Assert.Equal(16, db.ListLength(key));
        Assert.Equal("abcdefghijklmnop", string.Concat(db.ListRange(key)));
    }

    [Fact]
    public void ListLeftPushEmptyValues()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = db.ListLeftPush(key, Array.Empty<RedisValue>(), When.Always, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ListLeftPushKeyDoesNotExists()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = db.ListLeftPush(key, new RedisValue[] { "testvalue" }, When.Exists, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ListLeftPushToExisitingKey()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var pushResult = db.ListLeftPush(key, new RedisValue[] { "testvalue1" }, CommandFlags.None);
        Assert.Equal(1, pushResult);
        var pushXResult = db.ListLeftPush(key, new RedisValue[] { "testvalue2" }, When.Exists, CommandFlags.None);
        Assert.Equal(2, pushXResult);

        var rangeResult = db.ListRange(key, 0, -1);
        Assert.Equal(2, rangeResult.Length);
        Assert.Equal("testvalue2", rangeResult[0]);
        Assert.Equal("testvalue1", rangeResult[1]);
    }

    [Fact]
    public void ListLeftPushMultipleToExisitingKey()
    {
        using var conn = Create(require: RedisFeatures.v4_0_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var pushResult = db.ListLeftPush(key, new RedisValue[] { "testvalue1" }, CommandFlags.None);
        Assert.Equal(1, pushResult);
        var pushXResult = db.ListLeftPush(key, new RedisValue[] { "testvalue2", "testvalue3" }, When.Exists, CommandFlags.None);
        Assert.Equal(3, pushXResult);

        var rangeResult = db.ListRange(key, 0, -1);
        Assert.Equal(3, rangeResult.Length);
        Assert.Equal("testvalue3", rangeResult[0]);
        Assert.Equal("testvalue2", rangeResult[1]);
        Assert.Equal("testvalue1", rangeResult[2]);
    }

    [Fact]
    public async Task ListLeftPushAsyncEmptyValues()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = await db.ListLeftPushAsync(key, Array.Empty<RedisValue>(), When.Always, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListLeftPushAsyncKeyDoesNotExists()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = await db.ListLeftPushAsync(key, new RedisValue[] { "testvalue" }, When.Exists, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListLeftPushAsyncToExisitingKey()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var pushResult = await db.ListLeftPushAsync(key, new RedisValue[] { "testvalue1" }, CommandFlags.None);
        Assert.Equal(1, pushResult);
        var pushXResult = await db.ListLeftPushAsync(key, new RedisValue[] { "testvalue2" }, When.Exists, CommandFlags.None);
        Assert.Equal(2, pushXResult);

        var rangeResult = db.ListRange(key, 0, -1);
        Assert.Equal(2, rangeResult.Length);
        Assert.Equal("testvalue2", rangeResult[0]);
        Assert.Equal("testvalue1", rangeResult[1]);
    }

    [Fact]
    public async Task ListLeftPushAsyncMultipleToExisitingKey()
    {
        using var conn = Create(require: RedisFeatures.v4_0_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var pushResult = await db.ListLeftPushAsync(key, new RedisValue[] { "testvalue1" }, CommandFlags.None);
        Assert.Equal(1, pushResult);
        var pushXResult = await db.ListLeftPushAsync(key, new RedisValue[] { "testvalue2", "testvalue3" }, When.Exists, CommandFlags.None);
        Assert.Equal(3, pushXResult);

        var rangeResult = db.ListRange(key, 0, -1);
        Assert.Equal(3, rangeResult.Length);
        Assert.Equal("testvalue3", rangeResult[0]);
        Assert.Equal("testvalue2", rangeResult[1]);
        Assert.Equal("testvalue1", rangeResult[2]);
    }

    [Fact]
    public void ListRightPushEmptyValues()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = db.ListRightPush(key, Array.Empty<RedisValue>(), When.Always, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ListRightPushKeyDoesNotExists()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = db.ListRightPush(key, new RedisValue[] { "testvalue" }, When.Exists, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ListRightPushToExisitingKey()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var pushResult = db.ListRightPush(key, new RedisValue[] { "testvalue1" }, CommandFlags.None);
        Assert.Equal(1, pushResult);
        var pushXResult = db.ListRightPush(key, new RedisValue[] { "testvalue2" }, When.Exists, CommandFlags.None);
        Assert.Equal(2, pushXResult);

        var rangeResult = db.ListRange(key, 0, -1);
        Assert.Equal(2, rangeResult.Length);
        Assert.Equal("testvalue1", rangeResult[0]);
        Assert.Equal("testvalue2", rangeResult[1]);
    }

    [Fact]
    public void ListRightPushMultipleToExisitingKey()
    {
        using var conn = Create(require: RedisFeatures.v4_0_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var pushResult = db.ListRightPush(key, new RedisValue[] { "testvalue1" }, CommandFlags.None);
        Assert.Equal(1, pushResult);
        var pushXResult = db.ListRightPush(key, new RedisValue[] { "testvalue2", "testvalue3" }, When.Exists, CommandFlags.None);
        Assert.Equal(3, pushXResult);

        var rangeResult = db.ListRange(key, 0, -1);
        Assert.Equal(3, rangeResult.Length);
        Assert.Equal("testvalue1", rangeResult[0]);
        Assert.Equal("testvalue2", rangeResult[1]);
        Assert.Equal("testvalue3", rangeResult[2]);
    }

    [Fact]
    public async Task ListRightPushAsyncEmptyValues()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = await db.ListRightPushAsync(key, Array.Empty<RedisValue>(), When.Always, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListRightPushAsyncKeyDoesNotExists()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = await db.ListRightPushAsync(key, new RedisValue[] { "testvalue" }, When.Exists, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListRightPushAsyncToExisitingKey()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var pushResult = await db.ListRightPushAsync(key, new RedisValue[] { "testvalue1" }, CommandFlags.None);
        Assert.Equal(1, pushResult);
        var pushXResult = await db.ListRightPushAsync(key, new RedisValue[] { "testvalue2" }, When.Exists, CommandFlags.None);
        Assert.Equal(2, pushXResult);

        var rangeResult = db.ListRange(key, 0, -1);
        Assert.Equal(2, rangeResult.Length);
        Assert.Equal("testvalue1", rangeResult[0]);
        Assert.Equal("testvalue2", rangeResult[1]);
    }

    [Fact]
    public async Task ListRightPushAsyncMultipleToExisitingKey()
    {
        using var conn = Create(require: RedisFeatures.v4_0_0);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var pushResult = await db.ListRightPushAsync(key, new RedisValue[] { "testvalue1" }, CommandFlags.None);
        Assert.Equal(1, pushResult);
        var pushXResult = await db.ListRightPushAsync(key, new RedisValue[] { "testvalue2", "testvalue3" }, When.Exists, CommandFlags.None);
        Assert.Equal(3, pushXResult);

        var rangeResult = db.ListRange(key, 0, -1);
        Assert.Equal(3, rangeResult.Length);
        Assert.Equal("testvalue1", rangeResult[0]);
        Assert.Equal("testvalue2", rangeResult[1]);
        Assert.Equal("testvalue3", rangeResult[2]);
    }

    [Fact]
    public async Task ListMove()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        RedisKey src = Me();
        RedisKey dest = Me() + "dest";
        db.KeyDelete(src, CommandFlags.FireAndForget);

        var pushResult = await db.ListRightPushAsync(src, new RedisValue[] { "testvalue1", "testvalue2" });
        Assert.Equal(2, pushResult);

        var rangeResult1 = db.ListMove(src, dest, ListSide.Left, ListSide.Right);
        var rangeResult2 = db.ListMove(src, dest, ListSide.Left, ListSide.Left);
        var rangeResult3 = db.ListMove(dest, src, ListSide.Right, ListSide.Right);
        var rangeResult4 = db.ListMove(dest, src, ListSide.Right, ListSide.Left);
        Assert.Equal("testvalue1", rangeResult1);
        Assert.Equal("testvalue2", rangeResult2);
        Assert.Equal("testvalue1", rangeResult3);
        Assert.Equal("testvalue2", rangeResult4);
    }

    [Fact]
    public void ListMoveKeyDoesNotExist()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        RedisKey src = Me();
        RedisKey dest = Me() + "dest";
        db.KeyDelete(src, CommandFlags.FireAndForget);

        var rangeResult1 = db.ListMove(src, dest, ListSide.Left, ListSide.Right);
        Assert.True(rangeResult1.IsNull);
    }

    [Fact]
    public void ListPositionHappyPath()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string val = "foo";
        db.KeyDelete(key);

        db.ListLeftPush(key, val);
        var res = db.ListPosition(key, val);

        Assert.Equal(0, res);
    }

    [Fact]
    public void ListPositionEmpty()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string val = "foo";
        db.KeyDelete(key);

        var res = db.ListPosition(key, val);

        Assert.Equal(-1, res);
    }

    [Fact]
    public void ListPositionsHappyPath()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        db.KeyDelete(key);

        for (var i = 0; i < 10; i++)
        {
            db.ListLeftPush(key, foo);
            db.ListLeftPush(key, bar);
            db.ListLeftPush(key, baz);
        }

        var res = db.ListPositions(key, foo, 5);

        foreach (var item in res)
        {
            Assert.Equal(2, item % 3);
        }

        Assert.Equal(5, res.Length);
    }

    [Fact]
    public void ListPositionsTooFew()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        db.KeyDelete(key);

        for (var i = 0; i < 10; i++)
        {
            db.ListLeftPush(key, bar);
            db.ListLeftPush(key, baz);
        }

        db.ListLeftPush(key, foo);

        var res = db.ListPositions(key, foo, 5);
        Assert.Single(res);
        Assert.Equal(0, res.Single());
    }

    [Fact]
    public void ListPositionsAll()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        db.KeyDelete(key);

        for (var i = 0; i < 10; i++)
        {
            db.ListLeftPush(key, foo);
            db.ListLeftPush(key, bar);
            db.ListLeftPush(key, baz);
        }

        var res = db.ListPositions(key, foo, 0);

        foreach (var item in res)
        {
            Assert.Equal(2, item % 3);
        }

        Assert.Equal(10, res.Length);
    }

    [Fact]
    public void ListPositionsAllLimitLength()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        db.KeyDelete(key);

        for (var i = 0; i < 10; i++)
        {
            db.ListLeftPush(key, foo);
            db.ListLeftPush(key, bar);
            db.ListLeftPush(key, baz);
        }

        var res = db.ListPositions(key, foo, 0, maxLength: 15);

        foreach (var item in res)
        {
            Assert.Equal(2, item % 3);
        }

        Assert.Equal(5, res.Length);
    }

    [Fact]
    public void ListPositionsEmpty()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        db.KeyDelete(key);

        for (var i = 0; i < 10; i++)
        {
            db.ListLeftPush(key, bar);
            db.ListLeftPush(key, baz);
        }

        var res = db.ListPositions(key, foo, 5);

        Assert.Empty(res);
    }

    [Fact]
    public void ListPositionByRank()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        db.KeyDelete(key);

        for (var i = 0; i < 10; i++)
        {
            db.ListLeftPush(key, foo);
            db.ListLeftPush(key, bar);
            db.ListLeftPush(key, baz);
        }

        const int rank = 6;

        var res = db.ListPosition(key, foo, rank: rank);

        Assert.Equal((3 * rank) - 1, res);
    }

    [Fact]
    public void ListPositionLimitSoNull()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        db.KeyDelete(key);

        for (var i = 0; i < 10; i++)
        {
            db.ListLeftPush(key, bar);
            db.ListLeftPush(key, baz);
        }

        db.ListRightPush(key, foo);

        var res = db.ListPosition(key, foo, maxLength: 20);

        Assert.Equal(-1, res);
    }

    [Fact]
    public async Task ListPositionHappyPathAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string val = "foo";
        await db.KeyDeleteAsync(key);

        await db.ListLeftPushAsync(key, val);
        var res = await db.ListPositionAsync(key, val);

        Assert.Equal(0, res);
    }

    [Fact]
    public async Task ListPositionEmptyAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string val = "foo";
        await db.KeyDeleteAsync(key);

        var res = await db.ListPositionAsync(key, val);

        Assert.Equal(-1, res);
    }

    [Fact]
    public async Task ListPositionsHappyPathAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        await db.KeyDeleteAsync(key);

        for (var i = 0; i < 10; i++)
        {
            await db.ListLeftPushAsync(key, foo);
            await db.ListLeftPushAsync(key, bar);
            await db.ListLeftPushAsync(key, baz);
        }

        var res = await db.ListPositionsAsync(key, foo, 5);

        foreach (var item in res)
        {
            Assert.Equal(2, item % 3);
        }

        Assert.Equal(5, res.Length);
    }

    [Fact]
    public async Task ListPositionsTooFewAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        await db.KeyDeleteAsync(key);

        for (var i = 0; i < 10; i++)
        {
            await db.ListLeftPushAsync(key, bar);
            await db.ListLeftPushAsync(key, baz);
        }

        db.ListLeftPush(key, foo);

        var res = await db.ListPositionsAsync(key, foo, 5);
        Assert.Single(res);
        Assert.Equal(0, res.Single());
    }

    [Fact]
    public async Task ListPositionsAllAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        await db.KeyDeleteAsync(key);

        for (var i = 0; i < 10; i++)
        {
            await db.ListLeftPushAsync(key, foo);
            await db.ListLeftPushAsync(key, bar);
            await db.ListLeftPushAsync(key, baz);
        }

        var res = await db.ListPositionsAsync(key, foo, 0);

        foreach (var item in res)
        {
            Assert.Equal(2, item % 3);
        }

        Assert.Equal(10, res.Length);
    }

    [Fact]
    public async Task ListPositionsAllLimitLengthAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        await db.KeyDeleteAsync(key);

        for (var i = 0; i < 10; i++)
        {
            await db.ListLeftPushAsync(key, foo);
            await db.ListLeftPushAsync(key, bar);
            await db.ListLeftPushAsync(key, baz);
        }

        var res = await db.ListPositionsAsync(key, foo, 0, maxLength: 15);

        foreach (var item in res)
        {
            Assert.Equal(2, item % 3);
        }

        Assert.Equal(5, res.Length);
    }

    [Fact]
    public async Task ListPositionsEmptyAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        await db.KeyDeleteAsync(key);

        for (var i = 0; i < 10; i++)
        {
            await db.ListLeftPushAsync(key, bar);
            await db.ListLeftPushAsync(key, baz);
        }

        var res = await db.ListPositionsAsync(key, foo, 5);

        Assert.Empty(res);
    }

    [Fact]
    public async Task ListPositionByRankAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        await db.KeyDeleteAsync(key);

        for (var i = 0; i < 10; i++)
        {
            await db.ListLeftPushAsync(key, foo);
            await db.ListLeftPushAsync(key, bar);
            await db.ListLeftPushAsync(key, baz);
        }

        const int rank = 6;

        var res = await db.ListPositionAsync(key, foo, rank: rank);

        Assert.Equal((3 * rank) - 1, res);
    }

    [Fact]
    public async Task ListPositionLimitSoNullAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        await db.KeyDeleteAsync(key);

        for (var i = 0; i < 10; i++)
        {
            await db.ListLeftPushAsync(key, bar);
            await db.ListLeftPushAsync(key, baz);
        }

        await db.ListRightPushAsync(key, foo);

        var res = await db.ListPositionAsync(key, foo, maxLength: 20);

        Assert.Equal(-1, res);
    }

    [Fact]
    public async Task ListPositionFireAndForgetAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        await db.KeyDeleteAsync(key);

        for (var i = 0; i < 10; i++)
        {
            await db.ListLeftPushAsync(key, foo);
            await db.ListLeftPushAsync(key, bar);
            await db.ListLeftPushAsync(key, baz);
        }

        await db.ListRightPushAsync(key, foo);

        var res = await db.ListPositionAsync(key, foo, maxLength: 20, flags: CommandFlags.FireAndForget);

        Assert.Equal(-1, res);
    }

    [Fact]
    public void ListPositionFireAndForget()
    {
        using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string foo = "foo",
                     bar = "bar",
                     baz = "baz";

        db.KeyDelete(key);

        for (var i = 0; i < 10; i++)
        {
            db.ListLeftPush(key, foo);
            db.ListLeftPush(key, bar);
            db.ListLeftPush(key, baz);
        }

        db.ListRightPush(key, foo);

        var res = db.ListPosition(key, foo, maxLength: 20, flags: CommandFlags.FireAndForget);

        Assert.Equal(-1, res);
    }
}
