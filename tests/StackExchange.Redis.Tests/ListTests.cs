using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class ListTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task Ranges()
    {
        await using var conn = Create();

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
    public async Task ListLeftPushEmptyValues()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = db.ListLeftPush(key, Array.Empty<RedisValue>(), When.Always, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListLeftPushKeyDoesNotExists()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = db.ListLeftPush(key, new RedisValue[] { "testvalue" }, When.Exists, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListLeftPushToExisitingKey()
    {
        await using var conn = Create();

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
    public async Task ListLeftPushMultipleToExisitingKey()
    {
        await using var conn = Create(require: RedisFeatures.v4_0_0);

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
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = await db.ListLeftPushAsync(key, Array.Empty<RedisValue>(), When.Always, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListLeftPushAsyncKeyDoesNotExists()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = await db.ListLeftPushAsync(key, new RedisValue[] { "testvalue" }, When.Exists, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListLeftPushAsyncToExisitingKey()
    {
        await using var conn = Create();

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
        await using var conn = Create(require: RedisFeatures.v4_0_0);

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
    public async Task ListRightPushEmptyValues()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = db.ListRightPush(key, Array.Empty<RedisValue>(), When.Always, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListRightPushKeyDoesNotExists()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = db.ListRightPush(key, new RedisValue[] { "testvalue" }, When.Exists, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListRightPushToExisitingKey()
    {
        await using var conn = Create();

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
    public async Task ListRightPushMultipleToExisitingKey()
    {
        await using var conn = Create(require: RedisFeatures.v4_0_0);

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
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = await db.ListRightPushAsync(key, Array.Empty<RedisValue>(), When.Always, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListRightPushAsyncKeyDoesNotExists()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var result = await db.ListRightPushAsync(key, new RedisValue[] { "testvalue" }, When.Exists, CommandFlags.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ListRightPushAsyncToExisitingKey()
    {
        await using var conn = Create();

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
        await using var conn = Create(require: RedisFeatures.v4_0_0);

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
        await using var conn = Create(require: RedisFeatures.v6_2_0);

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
    public async Task ListMoveKeyDoesNotExist()
    {
        await using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        RedisKey src = Me();
        RedisKey dest = Me() + "dest";
        db.KeyDelete(src, CommandFlags.FireAndForget);

        var rangeResult1 = db.ListMove(src, dest, ListSide.Left, ListSide.Right);
        Assert.True(rangeResult1.IsNull);
    }

    [Fact]
    public async Task ListPositionHappyPath()
    {
        await using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string val = "foo";
        db.KeyDelete(key);

        db.ListLeftPush(key, val);
        var res = db.ListPosition(key, val);

        Assert.Equal(0, res);
    }

    [Fact]
    public async Task ListPositionEmpty()
    {
        await using var conn = Create(require: RedisFeatures.v6_0_6);

        var db = conn.GetDatabase();
        var key = Me();
        const string val = "foo";
        db.KeyDelete(key);

        var res = db.ListPosition(key, val);

        Assert.Equal(-1, res);
    }

    [Fact]
    public async Task ListPositionsHappyPath()
    {
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
    public async Task ListPositionsTooFew()
    {
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
    public async Task ListPositionsAll()
    {
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
    public async Task ListPositionsAllLimitLength()
    {
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
    public async Task ListPositionsEmpty()
    {
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
    public async Task ListPositionByRank()
    {
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
    public async Task ListPositionLimitSoNull()
    {
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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
    public async Task ListPositionFireAndForget()
    {
        await using var conn = Create(require: RedisFeatures.v6_0_6);

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

    [Fact]
    public async Task ListMultiPopSingleKeyAsync()
    {
        await using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync(key);

        db.ListLeftPush(key, "yankees");
        db.ListLeftPush(key, "blue jays");
        db.ListLeftPush(key, "orioles");
        db.ListLeftPush(key, "red sox");
        db.ListLeftPush(key, "rays");

        var res = await db.ListLeftPopAsync(new RedisKey[] { key }, 1);

        Assert.False(res.IsNull);
        Assert.Single(res.Values);
        Assert.Equal("rays", res.Values[0]);

        res = await db.ListRightPopAsync(new RedisKey[] { key }, 2);

        Assert.False(res.IsNull);
        Assert.Equal(2, res.Values.Length);
        Assert.Equal("yankees", res.Values[0]);
        Assert.Equal("blue jays", res.Values[1]);
    }

    [Fact]
    public async Task ListMultiPopMultipleKeysAsync()
    {
        await using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync(key);

        db.ListLeftPush(key, "yankees");
        db.ListLeftPush(key, "blue jays");
        db.ListLeftPush(key, "orioles");
        db.ListLeftPush(key, "red sox");
        db.ListLeftPush(key, "rays");

        var res = await db.ListLeftPopAsync(new RedisKey[] { "empty-key", key, "also-empty" }, 2);

        Assert.False(res.IsNull);
        Assert.Equal(2, res.Values.Length);
        Assert.Equal("rays", res.Values[0]);
        Assert.Equal("red sox", res.Values[1]);

        res = await db.ListRightPopAsync(new RedisKey[] { "empty-key", key, "also-empty" }, 1);

        Assert.False(res.IsNull);
        Assert.Single(res.Values);
        Assert.Equal("yankees", res.Values[0]);
    }

    [Fact]
    public async Task ListMultiPopSingleKey()
    {
        await using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key);

        db.ListLeftPush(key, "yankees");
        db.ListLeftPush(key, "blue jays");
        db.ListLeftPush(key, "orioles");
        db.ListLeftPush(key, "red sox");
        db.ListLeftPush(key, "rays");

        var res = db.ListLeftPop(new RedisKey[] { key }, 1);

        Assert.False(res.IsNull);
        Assert.Single(res.Values);
        Assert.Equal("rays", res.Values[0]);

        res = db.ListRightPop(new RedisKey[] { key }, 2);

        Assert.False(res.IsNull);
        Assert.Equal(2, res.Values.Length);
        Assert.Equal("yankees", res.Values[0]);
        Assert.Equal("blue jays", res.Values[1]);
    }

    [Fact]
    public async Task ListMultiPopZeroCount()
    {
        await using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key);

        var exception = await Assert.ThrowsAsync<RedisServerException>(() => db.ListLeftPopAsync(new RedisKey[] { key }, 0));
        Assert.Contains("ERR count should be greater than 0", exception.Message);
    }

    [Fact]
    public async Task ListMultiPopEmpty()
    {
        await using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key);

        var res = await db.ListLeftPopAsync(new RedisKey[] { key }, 1);
        Assert.True(res.IsNull);
    }

    [Fact]
    public async Task ListMultiPopEmptyKeys()
    {
        await using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => db.ListRightPop(Array.Empty<RedisKey>(), 5));
        Assert.Contains("keys must have a size of at least 1", exception.Message);

        exception = Assert.Throws<ArgumentOutOfRangeException>(() => db.ListLeftPop(Array.Empty<RedisKey>(), 5));
        Assert.Contains("keys must have a size of at least 1", exception.Message);
    }
}
