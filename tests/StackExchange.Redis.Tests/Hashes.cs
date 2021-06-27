using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Hashes : TestBase // https://redis.io/commands#hash
    {
        public Hashes(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public async Task TestIncrBy()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                _ = conn.KeyDeleteAsync(key).ForAwait();

                const int iterations = 100;
                var aTasks = new Task<long>[iterations];
                var bTasks = new Task<long>[iterations];
                for (int i = 1; i < iterations + 1; i++)
                {
                    aTasks[i - 1] = conn.HashIncrementAsync(key, "a", 1);
                    bTasks[i - 1] = conn.HashIncrementAsync(key, "b", -1);
                }
                await Task.WhenAll(bTasks).ForAwait();
                for (int i = 1; i < iterations + 1; i++)
                {
                    Assert.Equal(i, aTasks[i - 1].Result);
                    Assert.Equal(-i, bTasks[i - 1].Result);
                }
            }
        }

        [Fact]
        public async Task ScanAsync()
        {
            using (var muxer = Create())
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.Scan), r => r.Scan);
                var conn = muxer.GetDatabase();
                var key = Me();
                await conn.KeyDeleteAsync(key);
                for(int i = 0; i < 200; i++)
                {
                    await conn.HashSetAsync(key, "key" + i, "value " + i);
                }

                int count = 0;
                // works for async
                await foreach(var _ in conn.HashScanAsync(key, pageSize: 20))
                {
                    count++;
                }
                Assert.Equal(200, count);

                // and sync=>async (via cast)
                count = 0;
                await foreach (var _ in (IAsyncEnumerable<HashEntry>)conn.HashScan(key, pageSize: 20))
                {
                    count++;
                }
                Assert.Equal(200, count);

                // and sync (native)
                count = 0;
                foreach (var _ in conn.HashScan(key, pageSize: 20))
                {
                    count++;
                }
                Assert.Equal(200, count);

                // and async=>sync (via cast)
                count = 0;
                foreach (var _ in (IEnumerable<HashEntry>)conn.HashScanAsync(key, pageSize: 20))
                {
                    count++;
                }
                Assert.Equal(200, count);

            }
        }

        [Fact]
        public void Scan()
        {
            using (var muxer = Create())
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.Scan), r => r.Scan);
                var conn = muxer.GetDatabase();

                var key = Me();
                conn.KeyDeleteAsync(key);
                conn.HashSetAsync(key, "abc", "def");
                conn.HashSetAsync(key, "ghi", "jkl");
                conn.HashSetAsync(key, "mno", "pqr");

                var t1 = conn.HashScan(key);
                var t2 = conn.HashScan(key, "*h*");
                var t3 = conn.HashScan(key);
                var t4 = conn.HashScan(key, "*h*");

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
        }

        [Fact]
        public void TestIncrementOnHashThatDoesntExist()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                conn.KeyDeleteAsync("keynotexist");
                var result1 = conn.Wait(conn.HashIncrementAsync("keynotexist", "fieldnotexist", 1));
                var result2 = conn.Wait(conn.HashIncrementAsync("keynotexist", "anotherfieldnotexist", 1));
                Assert.Equal(1, result1);
                Assert.Equal(1, result2);
            }
        }

        [Fact]
        public async Task TestIncrByFloat()
        {
            using (var muxer = Create())
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.IncrementFloat), r => r.IncrementFloat);
                var conn = muxer.GetDatabase();
                var key = Me();
                _ = conn.KeyDeleteAsync(key).ForAwait();
                var aTasks = new Task<double>[1000];
                var bTasks = new Task<double>[1000];
                for (int i = 1; i < 1001; i++)
                {
                    aTasks[i-1] = conn.HashIncrementAsync(key, "a", 1.0);
                    bTasks[i-1] = conn.HashIncrementAsync(key, "b", -1.0);
                }
                await Task.WhenAll(bTasks).ForAwait();
                for (int i = 1; i < 1001; i++)
                {
                    Assert.Equal(i, aTasks[i-1].Result);
                    Assert.Equal(-i, bTasks[i-1].Result);
                }
            }
        }

        [Fact]
        public async Task TestGetAll()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                await conn.KeyDeleteAsync(key).ForAwait();
                var shouldMatch = new Dictionary<Guid, int>();
                var random = new Random();

                for (int i = 0; i < 1000; i++)
                {
                    var guid = Guid.NewGuid();
                    var value = random.Next(int.MaxValue);

                    shouldMatch[guid] = value;

                    _ = conn.HashIncrementAsync(key, guid.ToString(), value);
                }

                var inRedis = (await conn.HashGetAllAsync(key).ForAwait()).ToDictionary(
                    x => Guid.Parse(x.Name), x => int.Parse(x.Value));

                Assert.Equal(shouldMatch.Count, inRedis.Count);

                foreach (var k in shouldMatch.Keys)
                {
                    Assert.Equal(shouldMatch[k], inRedis[k]);
                }
            }
        }

        [Fact]
        public async Task TestGet()
        {
            using (var muxer = Create())
            {
                var key = Me();
                var conn = muxer.GetDatabase();
                var shouldMatch = new Dictionary<Guid, int>();
                var random = new Random();

                for (int i = 1; i < 1000; i++)
                {
                    var guid = Guid.NewGuid();
                    var value = random.Next(int.MaxValue);

                    shouldMatch[guid] = value;

                    _ = conn.HashIncrementAsync(key, guid.ToString(), value);
                }

                foreach (var k in shouldMatch.Keys)
                {
                    var inRedis = await conn.HashGetAsync(key, k.ToString()).ForAwait();
                    var num = int.Parse(inRedis);

                    Assert.Equal(shouldMatch[k], num);
                }
            }
        }

        [Fact]
        public async Task TestSet() // https://redis.io/commands/hset
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashkey = Me();
                var del =  conn.KeyDeleteAsync(hashkey).ForAwait();

                var val0 = conn.HashGetAsync(hashkey, "field").ForAwait();
                var set0 = conn.HashSetAsync(hashkey, "field", "value1").ForAwait();
                var val1 = conn.HashGetAsync(hashkey, "field").ForAwait();
                var set1 = conn.HashSetAsync(hashkey, "field", "value2").ForAwait();
                var val2 = conn.HashGetAsync(hashkey, "field").ForAwait();

                var set2 = conn.HashSetAsync(hashkey, "field-blob", Encoding.UTF8.GetBytes("value3")).ForAwait();
                var val3 = conn.HashGetAsync(hashkey, "field-blob").ForAwait();

                var set3 = conn.HashSetAsync(hashkey, "empty_type1", "").ForAwait();
                var val4 = conn.HashGetAsync(hashkey, "empty_type1").ForAwait();
                var set4 = conn.HashSetAsync(hashkey, "empty_type2", RedisValue.EmptyString).ForAwait();
                var val5 = conn.HashGetAsync(hashkey, "empty_type2").ForAwait();

                await del;
                Assert.Null((string)(await val0));
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
        }

        [Fact]
        public async Task TestSetNotExists() // https://redis.io/commands/hsetnx
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashkey = Me();
                var del = conn.KeyDeleteAsync(hashkey).ForAwait();

                var val0 = conn.HashGetAsync(hashkey, "field").ForAwait();
                var set0 = conn.HashSetAsync(hashkey, "field", "value1", When.NotExists).ForAwait();
                var val1 = conn.HashGetAsync(hashkey, "field").ForAwait();
                var set1 = conn.HashSetAsync(hashkey, "field", "value2", When.NotExists).ForAwait();
                var val2 = conn.HashGetAsync(hashkey, "field").ForAwait();

                var set2 = conn.HashSetAsync(hashkey, "field-blob", Encoding.UTF8.GetBytes("value3"), When.NotExists).ForAwait();
                var val3 = conn.HashGetAsync(hashkey, "field-blob").ForAwait();
                var set3 = conn.HashSetAsync(hashkey, "field-blob", Encoding.UTF8.GetBytes("value3"), When.NotExists).ForAwait();

                await del;
                Assert.Null((string)(await val0));
                Assert.True(await set0);
                Assert.Equal("value1", await val1);
                Assert.False(await set1);
                Assert.Equal("value1", await val2);

                Assert.True(await set2);
                Assert.Equal("value3", await val3);
                Assert.False(await set3);
            }
        }

        [Fact]
        public async Task TestDelSingle() // https://redis.io/commands/hdel
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashkey = Me();
                await conn.KeyDeleteAsync(hashkey).ForAwait();
                var del0 = conn.HashDeleteAsync(hashkey, "field").ForAwait();

                await conn.HashSetAsync(hashkey, "field", "value").ForAwait();

                var del1 = conn.HashDeleteAsync(hashkey, "field").ForAwait();
                var del2 = conn.HashDeleteAsync(hashkey, "field").ForAwait();

                Assert.False(await del0);
                Assert.True(await del1);
                Assert.False(await del2);
            }
        }

        [Fact]
        public async Task TestDelMulti() // https://redis.io/commands/hdel
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashkey = Me();
                conn.HashSet(hashkey, "key1", "val1", flags: CommandFlags.FireAndForget);
                conn.HashSet(hashkey, "key2", "val2", flags: CommandFlags.FireAndForget);
                conn.HashSet(hashkey, "key3", "val3", flags: CommandFlags.FireAndForget);

                var s1 = conn.HashExistsAsync(hashkey, "key1");
                var s2 = conn.HashExistsAsync(hashkey, "key2");
                var s3 = conn.HashExistsAsync(hashkey, "key3");

                var removed = conn.HashDeleteAsync(hashkey, new RedisValue[] { "key1", "key3" });

                var d1 = conn.HashExistsAsync(hashkey, "key1");
                var d2 = conn.HashExistsAsync(hashkey, "key2");
                var d3 = conn.HashExistsAsync(hashkey, "key3");

                Assert.True(await s1);
                Assert.True(await s2);
                Assert.True(await s3);

                Assert.Equal(2, await removed);

                Assert.False(await d1);
                Assert.True(await d2);
                Assert.False(await d3);

                var removeFinal = conn.HashDeleteAsync(hashkey, new RedisValue[] { "key2" });

                Assert.Equal(0, await conn.HashLengthAsync(hashkey).ForAwait());
                Assert.Equal(1, await removeFinal);
            }
        }

        [Fact]
        public async Task TestDelMultiInsideTransaction() // https://redis.io/commands/hdel
        {
            using (var outer = Create())
            {
                var conn = outer.GetDatabase().CreateTransaction();
                {
                    var hashkey = Me();
                    _ = conn.HashSetAsync(hashkey, "key1", "val1");
                    _ = conn.HashSetAsync(hashkey, "key2", "val2");
                    _ = conn.HashSetAsync(hashkey, "key3", "val3");

                    var s1 = conn.HashExistsAsync(hashkey, "key1");
                    var s2 = conn.HashExistsAsync(hashkey, "key2");
                    var s3 = conn.HashExistsAsync(hashkey, "key3");

                    var removed = conn.HashDeleteAsync(hashkey, new RedisValue[] { "key1", "key3" });

                    var d1 = conn.HashExistsAsync(hashkey, "key1");
                    var d2 = conn.HashExistsAsync(hashkey, "key2");
                    var d3 = conn.HashExistsAsync(hashkey, "key3");

                    conn.Execute();

                    Assert.True(await s1);
                    Assert.True(await s2);
                    Assert.True(await s3);

                    Assert.Equal(2, await removed);

                    Assert.False(await d1);
                    Assert.True(await d2);
                    Assert.False(await d3);
                }
            }
        }

        [Fact]
        public async Task TestExists() // https://redis.io/commands/hexists
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashkey = Me();
                _ = conn.KeyDeleteAsync(hashkey).ForAwait();
                var ex0 = conn.HashExistsAsync(hashkey, "field").ForAwait();
                _ = conn.HashSetAsync(hashkey, "field", "value").ForAwait();
                var ex1 = conn.HashExistsAsync(hashkey, "field").ForAwait();
                _ = conn.HashDeleteAsync(hashkey, "field").ForAwait();
                _ = conn.HashExistsAsync(hashkey, "field").ForAwait();

                Assert.False(await ex0);
                Assert.True(await ex1);
                Assert.False(await ex0);
            }
        }

        [Fact]
        public async Task TestHashKeys() // https://redis.io/commands/hkeys
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashKey = Me();
                await conn.KeyDeleteAsync(hashKey).ForAwait();

                var keys0 = await conn.HashKeysAsync(hashKey).ForAwait();
                Assert.Empty(keys0);

                await conn.HashSetAsync(hashKey, "foo", "abc").ForAwait();
                await conn.HashSetAsync(hashKey, "bar", "def").ForAwait();

                var keys1 = conn.HashKeysAsync(hashKey);

                var arr = await keys1;
                Assert.Equal(2, arr.Length);
                Assert.Equal("foo", arr[0]);
                Assert.Equal("bar", arr[1]);
            }
        }

        [Fact]
        public async Task TestHashValues() // https://redis.io/commands/hvals
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashkey = Me();
                await conn.KeyDeleteAsync(hashkey).ForAwait();

                var keys0 = await conn.HashValuesAsync(hashkey).ForAwait();

                await conn.HashSetAsync(hashkey, "foo", "abc").ForAwait();
                await conn.HashSetAsync(hashkey, "bar", "def").ForAwait();

                var keys1 = conn.HashValuesAsync(hashkey).ForAwait();

                Assert.Empty(keys0);

                var arr = await keys1;
                Assert.Equal(2, arr.Length);
                Assert.Equal("abc", Encoding.UTF8.GetString(arr[0]));
                Assert.Equal("def", Encoding.UTF8.GetString(arr[1]));
            }
        }

        [Fact]
        public async Task TestHashLength() // https://redis.io/commands/hlen
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashkey = Me();
                conn.KeyDelete(hashkey, CommandFlags.FireAndForget);

                var len0 = conn.HashLengthAsync(hashkey);

                conn.HashSet(hashkey, "foo", "abc", flags: CommandFlags.FireAndForget);
                conn.HashSet(hashkey, "bar", "def", flags: CommandFlags.FireAndForget);

                var len1 = conn.HashLengthAsync(hashkey);

                Assert.Equal(0, await len0);
                Assert.Equal(2, await len1);
            }
        }

        [Fact]
        public async Task TestGetMulti() // https://redis.io/commands/hmget
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashkey = Me();
                conn.KeyDelete(hashkey, CommandFlags.FireAndForget);

                RedisValue[] fields = { "foo", "bar", "blop" };
                var arr0 = await conn.HashGetAsync(hashkey, fields).ForAwait();

                conn.HashSet(hashkey, "foo", "abc", flags: CommandFlags.FireAndForget);
                conn.HashSet(hashkey, "bar", "def", flags: CommandFlags.FireAndForget);

                var arr1 = await conn.HashGetAsync(hashkey, fields).ForAwait();
                var arr2 = await conn.HashGetAsync(hashkey, fields).ForAwait();

                Assert.Equal(3, arr0.Length);
                Assert.Null((string)arr0[0]);
                Assert.Null((string)arr0[1]);
                Assert.Null((string)arr0[2]);

                Assert.Equal(3, arr1.Length);
                Assert.Equal("abc", arr1[0]);
                Assert.Equal("def", arr1[1]);
                Assert.Null((string)arr1[2]);

                Assert.Equal(3, arr2.Length);
                Assert.Equal("abc", arr2[0]);
                Assert.Equal("def", arr2[1]);
                Assert.Null((string)arr2[2]);
            }
        }

        [Fact]
        public void TestGetPairs() // https://redis.io/commands/hgetall
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashkey = Me();
                conn.KeyDeleteAsync(hashkey);

                var result0 = conn.HashGetAllAsync(hashkey);

                conn.HashSetAsync(hashkey, "foo", "abc");
                conn.HashSetAsync(hashkey, "bar", "def");

                var result1 = conn.HashGetAllAsync(hashkey);

                Assert.Empty(muxer.Wait(result0));
                var result = muxer.Wait(result1).ToStringDictionary();
                Assert.Equal(2, result.Count);
                Assert.Equal("abc", result["foo"]);
                Assert.Equal("def", result["bar"]);
            }
        }

        [Fact]
        public void TestSetPairs() // https://redis.io/commands/hmset
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashkey = Me();
                conn.KeyDeleteAsync(hashkey).ForAwait();

                var result0 = conn.HashGetAllAsync(hashkey);

                var data = new [] {
                    new HashEntry("foo", Encoding.UTF8.GetBytes("abc")),
                    new HashEntry("bar", Encoding.UTF8.GetBytes("def"))
                };
                conn.HashSetAsync(hashkey, data).ForAwait();

                var result1 = conn.Wait(conn.HashGetAllAsync(hashkey));

                Assert.Empty(result0.Result);
                var result = result1.ToStringDictionary();
                Assert.Equal(2, result.Count);
                Assert.Equal("abc", result["foo"]);
                Assert.Equal("def", result["bar"]);
            }
        }

        [Fact]
        public async Task TestWhenAlwaysAsync()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var hashkey = Me();
                conn.KeyDelete(hashkey, CommandFlags.FireAndForget);

                var result1 = await conn.HashSetAsync(hashkey, "foo", "bar", When.Always, CommandFlags.None);
                var result2 = await conn.HashSetAsync(hashkey, "foo2", "bar", When.Always, CommandFlags.None);
                var result3 = await conn.HashSetAsync(hashkey, "foo", "bar", When.Always, CommandFlags.None);
                var result4 = await conn.HashSetAsync(hashkey, "foo", "bar2", When.Always, CommandFlags.None);

                Assert.True(result1, "Initial set key 1");
                Assert.True(result2, "Initial set key 2");
                // Fields modified *but not added* should be a zero/false. That's the behavior of HSET
                Assert.False(result3, "Duplicate set key 1");
                Assert.False(result4, "Duplicate se key 1 variant");
            }
        }
    }
}
