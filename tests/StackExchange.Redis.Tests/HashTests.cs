using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for <see href="https://redis.io/commands#hash"/>.
/// </summary>
[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class HashTests : TestBase
{
    public HashTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public async Task TestIncrBy()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        _ = db.KeyDeleteAsync(key).ForAwait();

        const int iterations = 100;
        var aTasks = new Task<long>[iterations];
        var bTasks = new Task<long>[iterations];
        for (int i = 1; i < iterations + 1; i++)
        {
            aTasks[i - 1] = db.HashIncrementAsync(key, "a", 1);
            bTasks[i - 1] = db.HashIncrementAsync(key, "b", -1);
        }
        await Task.WhenAll(bTasks).ForAwait();
        for (int i = 1; i < iterations + 1; i++)
        {
            Assert.Equal(i, aTasks[i - 1].Result);
            Assert.Equal(-i, bTasks[i - 1].Result);
        }
    }

    [Fact]
    public async Task ScanAsync()
    {
        using var conn = Create(require: RedisFeatures.v2_8_0);

        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync(key);
        for (int i = 0; i < 200; i++)
        {
            await db.HashSetAsync(key, "key" + i, "value " + i);
        }

        int count = 0;
        // works for async
        await foreach (var _ in db.HashScanAsync(key, pageSize: 20))
        {
            count++;
        }
        Assert.Equal(200, count);

        // and sync=>async (via cast)
        count = 0;
        await foreach (var _ in (IAsyncEnumerable<HashEntry>)db.HashScan(key, pageSize: 20))
        {
            count++;
        }
        Assert.Equal(200, count);

        // and sync (native)
        count = 0;
        foreach (var _ in db.HashScan(key, pageSize: 20))
        {
            count++;
        }
        Assert.Equal(200, count);

        // and async=>sync (via cast)
        count = 0;
        foreach (var _ in (IEnumerable<HashEntry>)db.HashScanAsync(key, pageSize: 20))
        {
            count++;
        }
        Assert.Equal(200, count);
    }

    [Fact]
    public void Scan()
    {
        using var conn = Create(require: RedisFeatures.v2_8_0);

        var db = conn.GetDatabase();

        var key = Me();
        db.KeyDeleteAsync(key);
        db.HashSetAsync(key, "abc", "def");
        db.HashSetAsync(key, "ghi", "jkl");
        db.HashSetAsync(key, "mno", "pqr");

        var t1 = db.HashScan(key);
        var t2 = db.HashScan(key, "*h*");
        var t3 = db.HashScan(key);
        var t4 = db.HashScan(key, "*h*");

        var v1 = t1.ToArray();
        var v2 = t2.ToArray();
        var v3 = t3.ToArray();
        var v4 = t4.ToArray();

        Assert.Equal(3, v1.Length);
        Assert.Single(v2);
        Assert.Equal(3, v3.Length);
        Assert.Single(v4);
        Array.Sort(v1, (x, y) => string.Compare(x.Name, y.Name));
        Array.Sort(v2, (x, y) => string.Compare(x.Name, y.Name));
        Array.Sort(v3, (x, y) => string.Compare(x.Name, y.Name));
        Array.Sort(v4, (x, y) => string.Compare(x.Name, y.Name));

        Assert.Equal("abc=def,ghi=jkl,mno=pqr", string.Join(",", v1.Select(pair => pair.Name + "=" + pair.Value)));
        Assert.Equal("ghi=jkl", string.Join(",", v2.Select(pair => pair.Name + "=" + pair.Value)));
        Assert.Equal("abc=def,ghi=jkl,mno=pqr", string.Join(",", v3.Select(pair => pair.Name + "=" + pair.Value)));
        Assert.Equal("ghi=jkl", string.Join(",", v4.Select(pair => pair.Name + "=" + pair.Value)));
    }

    [Fact]
    public void TestIncrementOnHashThatDoesntExist()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        db.KeyDeleteAsync("keynotexist");
        var result1 = db.Wait(db.HashIncrementAsync("keynotexist", "fieldnotexist", 1));
        var result2 = db.Wait(db.HashIncrementAsync("keynotexist", "anotherfieldnotexist", 1));
        Assert.Equal(1, result1);
        Assert.Equal(1, result2);
    }

    [Fact]
    public async Task TestIncrByFloat()
    {
        using var conn = Create(require: RedisFeatures.v2_6_0);

        var db = conn.GetDatabase();
        var key = Me();
        _ = db.KeyDeleteAsync(key).ForAwait();
        var aTasks = new Task<double>[1000];
        var bTasks = new Task<double>[1000];
        for (int i = 1; i < 1001; i++)
        {
            aTasks[i - 1] = db.HashIncrementAsync(key, "a", 1.0);
            bTasks[i - 1] = db.HashIncrementAsync(key, "b", -1.0);
        }
        await Task.WhenAll(bTasks).ForAwait();
        for (int i = 1; i < 1001; i++)
        {
            Assert.Equal(i, aTasks[i - 1].Result);
            Assert.Equal(-i, bTasks[i - 1].Result);
        }
    }

    [Fact]
    public async Task TestGetAll()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync(key).ForAwait();
        var shouldMatch = new Dictionary<Guid, int>();
        var random = new Random();

        for (int i = 0; i < 1000; i++)
        {
            var guid = Guid.NewGuid();
            var value = random.Next(int.MaxValue);

            shouldMatch[guid] = value;

            _ = db.HashIncrementAsync(key, guid.ToString(), value);
        }

        var inRedis = (await db.HashGetAllAsync(key).ForAwait()).ToDictionary(
            x => Guid.Parse(x.Name!), x => int.Parse(x.Value!));

        Assert.Equal(shouldMatch.Count, inRedis.Count);

        foreach (var k in shouldMatch.Keys)
        {
            Assert.Equal(shouldMatch[k], inRedis[k]);
        }
    }

    [Fact]
    public async Task TestGet()
    {
        using var conn = Create();

        var key = Me();
        var db = conn.GetDatabase();
        var shouldMatch = new Dictionary<Guid, int>();
        var random = new Random();

        for (int i = 1; i < 1000; i++)
        {
            var guid = Guid.NewGuid();
            var value = random.Next(int.MaxValue);

            shouldMatch[guid] = value;

            _ = db.HashIncrementAsync(key, guid.ToString(), value);
        }

        foreach (var k in shouldMatch.Keys)
        {
            var inRedis = await db.HashGetAsync(key, k.ToString()).ForAwait();
            var num = int.Parse(inRedis!);

            Assert.Equal(shouldMatch[k], num);
        }
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hset"/>.
    /// </summary>
    [Fact]
    public async Task TestSet()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashkey = Me();
        var del = db.KeyDeleteAsync(hashkey).ForAwait();

        var val0 = db.HashGetAsync(hashkey, "field").ForAwait();
        var set0 = db.HashSetAsync(hashkey, "field", "value1").ForAwait();
        var val1 = db.HashGetAsync(hashkey, "field").ForAwait();
        var set1 = db.HashSetAsync(hashkey, "field", "value2").ForAwait();
        var val2 = db.HashGetAsync(hashkey, "field").ForAwait();

        var set2 = db.HashSetAsync(hashkey, "field-blob", Encoding.UTF8.GetBytes("value3")).ForAwait();
        var val3 = db.HashGetAsync(hashkey, "field-blob").ForAwait();

        var set3 = db.HashSetAsync(hashkey, "empty_type1", "").ForAwait();
        var val4 = db.HashGetAsync(hashkey, "empty_type1").ForAwait();
        var set4 = db.HashSetAsync(hashkey, "empty_type2", RedisValue.EmptyString).ForAwait();
        var val5 = db.HashGetAsync(hashkey, "empty_type2").ForAwait();

        await del;
        Assert.Null((string?)(await val0));
        Assert.True(await set0);
        Assert.Equal("value1", await val1);
        Assert.False(await set1);
        Assert.Equal("value2", await val2);

        Assert.True(await set2);
        Assert.Equal("value3", await val3);

        Assert.True(await set3);
        Assert.Equal("", await val4);
        Assert.True(await set4);
        Assert.Equal("", await val5);
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hsetnx"/>.
    /// </summary>
    [Fact]
    public async Task TestSetNotExists()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashkey = Me();
        var del = db.KeyDeleteAsync(hashkey).ForAwait();

        var val0 = db.HashGetAsync(hashkey, "field").ForAwait();
        var set0 = db.HashSetAsync(hashkey, "field", "value1", When.NotExists).ForAwait();
        var val1 = db.HashGetAsync(hashkey, "field").ForAwait();
        var set1 = db.HashSetAsync(hashkey, "field", "value2", When.NotExists).ForAwait();
        var val2 = db.HashGetAsync(hashkey, "field").ForAwait();

        var set2 = db.HashSetAsync(hashkey, "field-blob", Encoding.UTF8.GetBytes("value3"), When.NotExists).ForAwait();
        var val3 = db.HashGetAsync(hashkey, "field-blob").ForAwait();
        var set3 = db.HashSetAsync(hashkey, "field-blob", Encoding.UTF8.GetBytes("value3"), When.NotExists).ForAwait();

        await del;
        Assert.Null((string?)(await val0));
        Assert.True(await set0);
        Assert.Equal("value1", await val1);
        Assert.False(await set1);
        Assert.Equal("value1", await val2);

        Assert.True(await set2);
        Assert.Equal("value3", await val3);
        Assert.False(await set3);
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hdel"/>.
    /// </summary>
    [Fact]
    public async Task TestDelSingle()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashkey = Me();
        await db.KeyDeleteAsync(hashkey).ForAwait();
        var del0 = db.HashDeleteAsync(hashkey, "field").ForAwait();

        await db.HashSetAsync(hashkey, "field", "value").ForAwait();

        var del1 = db.HashDeleteAsync(hashkey, "field").ForAwait();
        var del2 = db.HashDeleteAsync(hashkey, "field").ForAwait();

        Assert.False(await del0);
        Assert.True(await del1);
        Assert.False(await del2);
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hdel"/>.
    /// </summary>
    [Fact]
    public async Task TestDelMulti()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashkey = Me();
        db.HashSet(hashkey, "key1", "val1", flags: CommandFlags.FireAndForget);
        db.HashSet(hashkey, "key2", "val2", flags: CommandFlags.FireAndForget);
        db.HashSet(hashkey, "key3", "val3", flags: CommandFlags.FireAndForget);

        var s1 = db.HashExistsAsync(hashkey, "key1");
        var s2 = db.HashExistsAsync(hashkey, "key2");
        var s3 = db.HashExistsAsync(hashkey, "key3");

        var removed = db.HashDeleteAsync(hashkey, new RedisValue[] { "key1", "key3" });

        var d1 = db.HashExistsAsync(hashkey, "key1");
        var d2 = db.HashExistsAsync(hashkey, "key2");
        var d3 = db.HashExistsAsync(hashkey, "key3");

        Assert.True(await s1);
        Assert.True(await s2);
        Assert.True(await s3);

        Assert.Equal(2, await removed);

        Assert.False(await d1);
        Assert.True(await d2);
        Assert.False(await d3);

        var removeFinal = db.HashDeleteAsync(hashkey, new RedisValue[] { "key2" });

        Assert.Equal(0, await db.HashLengthAsync(hashkey).ForAwait());
        Assert.Equal(1, await removeFinal);
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hdel"/>.
    /// </summary>
    [Fact]
    public async Task TestDelMultiInsideTransaction()
    {
        using var conn = Create();

        var tran = conn.GetDatabase().CreateTransaction();
        {
            var hashkey = Me();
            _ = tran.HashSetAsync(hashkey, "key1", "val1");
            _ = tran.HashSetAsync(hashkey, "key2", "val2");
            _ = tran.HashSetAsync(hashkey, "key3", "val3");

            var s1 = tran.HashExistsAsync(hashkey, "key1");
            var s2 = tran.HashExistsAsync(hashkey, "key2");
            var s3 = tran.HashExistsAsync(hashkey, "key3");

            var removed = tran.HashDeleteAsync(hashkey, new RedisValue[] { "key1", "key3" });

            var d1 = tran.HashExistsAsync(hashkey, "key1");
            var d2 = tran.HashExistsAsync(hashkey, "key2");
            var d3 = tran.HashExistsAsync(hashkey, "key3");

            tran.Execute();

            Assert.True(await s1);
            Assert.True(await s2);
            Assert.True(await s3);

            Assert.Equal(2, await removed);

            Assert.False(await d1);
            Assert.True(await d2);
            Assert.False(await d3);
        }
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hexists"/>.
    /// </summary>
    [Fact]
    public async Task TestExists()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashkey = Me();
        _ = db.KeyDeleteAsync(hashkey).ForAwait();
        var ex0 = db.HashExistsAsync(hashkey, "field").ForAwait();
        _ = db.HashSetAsync(hashkey, "field", "value").ForAwait();
        var ex1 = db.HashExistsAsync(hashkey, "field").ForAwait();
        _ = db.HashDeleteAsync(hashkey, "field").ForAwait();
        _ = db.HashExistsAsync(hashkey, "field").ForAwait();

        Assert.False(await ex0);
        Assert.True(await ex1);
        Assert.False(await ex0);
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hkeys"/>.
    /// </summary>
    [Fact]
    public async Task TestHashKeys()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashKey = Me();
        await db.KeyDeleteAsync(hashKey).ForAwait();

        var keys0 = await db.HashKeysAsync(hashKey).ForAwait();
        Assert.Empty(keys0);

        await db.HashSetAsync(hashKey, "foo", "abc").ForAwait();
        await db.HashSetAsync(hashKey, "bar", "def").ForAwait();

        var keys1 = db.HashKeysAsync(hashKey);

        var arr = await keys1;
        Assert.Equal(2, arr.Length);
        Assert.Equal("foo", arr[0]);
        Assert.Equal("bar", arr[1]);
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hvals"/>.
    /// </summary>
    [Fact]
    public async Task TestHashValues()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashkey = Me();
        await db.KeyDeleteAsync(hashkey).ForAwait();

        var keys0 = await db.HashValuesAsync(hashkey).ForAwait();

        await db.HashSetAsync(hashkey, "foo", "abc").ForAwait();
        await db.HashSetAsync(hashkey, "bar", "def").ForAwait();

        var keys1 = db.HashValuesAsync(hashkey).ForAwait();

        Assert.Empty(keys0);

        var arr = await keys1;
        Assert.Equal(2, arr.Length);
        Assert.Equal("abc", Encoding.UTF8.GetString(arr[0]!));
        Assert.Equal("def", Encoding.UTF8.GetString(arr[1]!));
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hlen"/>.
    /// </summary>
    [Fact]
    public async Task TestHashLength()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashkey = Me();
        db.KeyDelete(hashkey, CommandFlags.FireAndForget);

        var len0 = db.HashLengthAsync(hashkey);

        db.HashSet(hashkey, "foo", "abc", flags: CommandFlags.FireAndForget);
        db.HashSet(hashkey, "bar", "def", flags: CommandFlags.FireAndForget);

        var len1 = db.HashLengthAsync(hashkey);

        Assert.Equal(0, await len0);
        Assert.Equal(2, await len1);
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hmget"/>.
    /// </summary>
    [Fact]
    public async Task TestGetMulti()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashkey = Me();
        db.KeyDelete(hashkey, CommandFlags.FireAndForget);

        RedisValue[] fields = { "foo", "bar", "blop" };
        var arr0 = await db.HashGetAsync(hashkey, fields).ForAwait();

        db.HashSet(hashkey, "foo", "abc", flags: CommandFlags.FireAndForget);
        db.HashSet(hashkey, "bar", "def", flags: CommandFlags.FireAndForget);

        var arr1 = await db.HashGetAsync(hashkey, fields).ForAwait();
        var arr2 = await db.HashGetAsync(hashkey, fields).ForAwait();

        Assert.Equal(3, arr0.Length);
        Assert.Null((string?)arr0[0]);
        Assert.Null((string?)arr0[1]);
        Assert.Null((string?)arr0[2]);

        Assert.Equal(3, arr1.Length);
        Assert.Equal("abc", arr1[0]);
        Assert.Equal("def", arr1[1]);
        Assert.Null((string?)arr1[2]);

        Assert.Equal(3, arr2.Length);
        Assert.Equal("abc", arr2[0]);
        Assert.Equal("def", arr2[1]);
        Assert.Null((string?)arr2[2]);
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hgetall"/>.
    /// </summary>
    [Fact]
    public void TestGetPairs()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashkey = Me();
        db.KeyDeleteAsync(hashkey);

        var result0 = db.HashGetAllAsync(hashkey);

        db.HashSetAsync(hashkey, "foo", "abc");
        db.HashSetAsync(hashkey, "bar", "def");

        var result1 = db.HashGetAllAsync(hashkey);

        Assert.Empty(conn.Wait(result0));
        var result = conn.Wait(result1).ToStringDictionary();
        Assert.Equal(2, result.Count);
        Assert.Equal("abc", result["foo"]);
        Assert.Equal("def", result["bar"]);
    }

    /// <summary>
    /// Tests for <see href="https://redis.io/commands/hmset"/>.
    /// </summary>
    [Fact]
    public void TestSetPairs()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashkey = Me();
        db.KeyDeleteAsync(hashkey).ForAwait();

        var result0 = db.HashGetAllAsync(hashkey);

        var data = new[] {
                new HashEntry("foo", Encoding.UTF8.GetBytes("abc")),
                new HashEntry("bar", Encoding.UTF8.GetBytes("def"))
            };
        db.HashSetAsync(hashkey, data).ForAwait();

        var result1 = db.Wait(db.HashGetAllAsync(hashkey));

        Assert.Empty(result0.Result);
        var result = result1.ToStringDictionary();
        Assert.Equal(2, result.Count);
        Assert.Equal("abc", result["foo"]);
        Assert.Equal("def", result["bar"]);
    }

    [Fact]
    public async Task TestWhenAlwaysAsync()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var hashkey = Me();
        db.KeyDelete(hashkey, CommandFlags.FireAndForget);

        var result1 = await db.HashSetAsync(hashkey, "foo", "bar", When.Always, CommandFlags.None);
        var result2 = await db.HashSetAsync(hashkey, "foo2", "bar", When.Always, CommandFlags.None);
        var result3 = await db.HashSetAsync(hashkey, "foo", "bar", When.Always, CommandFlags.None);
        var result4 = await db.HashSetAsync(hashkey, "foo", "bar2", When.Always, CommandFlags.None);

        Assert.True(result1, "Initial set key 1");
        Assert.True(result2, "Initial set key 2");
        // Fields modified *but not added* should be a zero/false. That's the behavior of HSET
        Assert.False(result3, "Duplicate set key 1");
        Assert.False(result4, "Duplicate se key 1 variant");
    }

    [Fact]
    public async Task HashRandomFieldAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var hashKey = Me();
        var items = new HashEntry[] { new("new york", "yankees"), new("baltimore", "orioles"), new("boston", "red sox"), new("Tampa Bay", "rays"), new("Toronto", "blue jays") };
        await db.HashSetAsync(hashKey, items);

        var singleField = await db.HashRandomFieldAsync(hashKey);
        var multiFields = await db.HashRandomFieldsAsync(hashKey, 3);
        var withValues = await db.HashRandomFieldsWithValuesAsync(hashKey, 3);
        Assert.Equal(3, multiFields.Length);
        Assert.Equal(3, withValues.Length);
        Assert.Contains(items, x => x.Name == singleField);

        foreach (var field in multiFields)
        {
            Assert.Contains(items, x => x.Name == field);
        }

        foreach (var field in withValues)
        {
            Assert.Contains(items, x => x.Name == field.Name);
        }
    }

    [Fact]
    public void HashRandomField()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var hashKey = Me();
        var items = new HashEntry[] { new("new york", "yankees"), new("baltimore", "orioles"), new("boston", "red sox"), new("Tampa Bay", "rays"), new("Toronto", "blue jays") };
        db.HashSet(hashKey, items);

        var singleField = db.HashRandomField(hashKey);
        var multiFields = db.HashRandomFields(hashKey, 3);
        var withValues = db.HashRandomFieldsWithValues(hashKey, 3);
        Assert.Equal(3, multiFields.Length);
        Assert.Equal(3, withValues.Length);
        Assert.Contains(items, x => x.Name == singleField);

        foreach (var field in multiFields)
        {
            Assert.Contains(items, x => x.Name == field);
        }

        foreach (var field in withValues)
        {
            Assert.Contains(items, x => x.Name == field.Name);
        }
    }

    [Fact]
    public void HashRandomFieldEmptyHash()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var hashKey = Me();

        var singleField = db.HashRandomField(hashKey);
        var multiFields = db.HashRandomFields(hashKey, 3);
        var withValues = db.HashRandomFieldsWithValues(hashKey, 3);

        Assert.Equal(RedisValue.Null, singleField);
        Assert.Empty(multiFields);
        Assert.Empty(withValues);
    }
}
