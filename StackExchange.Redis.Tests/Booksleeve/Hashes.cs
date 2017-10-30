using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve
{
    public class Hashes : BookSleeveTestBase // https://redis.io/commands#hash
    {
        public Hashes(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestIncrBy()
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(5);
                conn.KeyDeleteAsync("hash-test");
                for (int i = 1; i < 1000; i++)
                {
                    Assert.Equal(i, conn.HashIncrementAsync("hash-test", "a", 1).Result);
                    Assert.Equal(-i, conn.HashIncrementAsync("hash-test", "b", -1).Result);
                    //Assert.Equal(i, conn.Wait(conn.Hashes.Increment(5, "hash-test", "a", 1)));
                    //Assert.Equal(-i, conn.Wait(conn.Hashes.Increment(5, "hash-test", "b", -1)));
                }
            }
        }

        [Fact]
        public void Scan()
        {
            using (var muxer = GetUnsecuredConnection(waitForOpen: true))
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.Scan), r => r.Scan);

                const int db = 3;
                var conn = muxer.GetDatabase(db);

                const string key = "hash-scan";
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
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(0);
                conn.KeyDeleteAsync("keynotexist");
                var result1 = conn.Wait(conn.HashIncrementAsync("keynotexist", "fieldnotexist", 1));
                var result2 = conn.Wait(conn.HashIncrementAsync("keynotexist", "anotherfieldnotexist", 1));
                Assert.Equal(1, result1);
                Assert.Equal(1, result2);
            }
        }

        [Fact]
        public void TestIncrByFloat()
        {
            using (var muxer = GetUnsecuredConnection(waitForOpen: true))
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.IncrementFloat), r => r.IncrementFloat);
                var conn = muxer.GetDatabase(5);
                conn.KeyDeleteAsync("hash-test");
                for (int i = 1; i < 1000; i++)
                {
                    Assert.Equal(i, conn.HashIncrementAsync("hash-test", "a", 1.0).Result);
                    Assert.Equal(-i, conn.HashIncrementAsync("hash-test", "b", -1.0).Result);
                }
            }
        }

        [Fact]
        public void TestGetAll()
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(6);
                const string key = "hash test";
                conn.KeyDeleteAsync(key);
                var shouldMatch = new Dictionary<Guid, int>();
                var random = new Random();

                for (int i = 1; i < 1000; i++)
                {
                    var guid = Guid.NewGuid();
                    var value = random.Next(Int32.MaxValue);

                    shouldMatch[guid] = value;

                    var x = conn.HashIncrementAsync(key, guid.ToString(), value).Result; // Kill Async
                }
#pragma warning disable 618
                var inRedis = conn.HashGetAllAsync(key).Result.ToDictionary(
                    x => Guid.Parse(x.Name), x => int.Parse(x.Value));
#pragma warning restore 618

                Assert.Equal(shouldMatch.Count, inRedis.Count);

                foreach (var k in shouldMatch.Keys)
                {
                    Assert.Equal(shouldMatch[k], inRedis[k]);
                }
            }
        }

        [Fact]
        public void TestGet()
        {
            using (var muxer = GetUnsecuredConnection())
            {
                const string key = "hash test";
                var conn = muxer.GetDatabase(6);
                var shouldMatch = new Dictionary<Guid, int>();
                var random = new Random();

                for (int i = 1; i < 1000; i++)
                {
                    var guid = Guid.NewGuid();
                    var value = random.Next(Int32.MaxValue);

                    shouldMatch[guid] = value;

                    var x = conn.HashIncrementAsync(key, guid.ToString(), value).Result; // Kill Async
                }

                foreach (var k in shouldMatch.Keys)
                {
                    var inRedis = conn.HashGetAsync(key, k.ToString()).Result;
                    var num = int.Parse(inRedis);

                    Assert.Equal(shouldMatch[k], num);
                }
            }
        }

        [Fact]
        public void TestSet() // https://redis.io/commands/hset
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(9);
                conn.KeyDeleteAsync("hashkey");

                var val0 = conn.HashGetAsync("hashkey", "field");
                var set0 = conn.HashSetAsync("hashkey", "field", "value1");
                var val1 = conn.HashGetAsync("hashkey", "field");
                var set1 = conn.HashSetAsync("hashkey", "field", "value2");
                var val2 = conn.HashGetAsync("hashkey", "field");

                var set2 = conn.HashSetAsync("hashkey", "field-blob", Encoding.UTF8.GetBytes("value3"));
                var val3 = conn.HashGetAsync("hashkey", "field-blob");

                var set3 = conn.HashSetAsync("hashkey", "empty_type1", "");
                var val4 = conn.HashGetAsync("hashkey", "empty_type1");
                var set4 = conn.HashSetAsync("hashkey", "empty_type2", RedisValue.EmptyString);
                var val5 = conn.HashGetAsync("hashkey", "empty_type2");

                Assert.Null((string)val0.Result);
                Assert.True(set0.Result);
                Assert.Equal("value1", val1.Result);
                Assert.False(set1.Result);
                Assert.Equal("value2", val2.Result);

                Assert.True(set2.Result);
                Assert.Equal("value3", val3.Result);

                Assert.True(set3.Result);
                Assert.Equal("", val4.Result);
                Assert.True(set4.Result);
                Assert.Equal("", val5.Result);
            }
        }

        [Fact]
        public void TestSetNotExists() // https://redis.io/commands/hsetnx
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(9);
                conn.KeyDeleteAsync("hashkey");

                var val0 = conn.HashGetAsync("hashkey", "field");
                var set0 = conn.HashSetAsync("hashkey", "field", "value1", When.NotExists);
                var val1 = conn.HashGetAsync("hashkey", "field");
                var set1 = conn.HashSetAsync("hashkey", "field", "value2", When.NotExists);
                var val2 = conn.HashGetAsync("hashkey", "field");

                var set2 = conn.HashSetAsync("hashkey", "field-blob", Encoding.UTF8.GetBytes("value3"), When.NotExists);
                var val3 = conn.HashGetAsync("hashkey", "field-blob");
                var set3 = conn.HashSetAsync("hashkey", "field-blob", Encoding.UTF8.GetBytes("value3"), When.NotExists);

                Assert.Null((string)val0.Result);
                Assert.True(set0.Result);
                Assert.Equal("value1", val1.Result);
                Assert.False(set1.Result);
                Assert.Equal("value1", val2.Result);

                Assert.True(set2.Result);
                Assert.Equal("value3", val3.Result);
                Assert.False(set3.Result);
            }
        }

        [Fact]
        public void TestDelSingle() // https://redis.io/commands/hdel
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(9);
                conn.KeyDeleteAsync("hashkey");
                var del0 = conn.HashDeleteAsync("hashkey", "field");

                conn.HashSetAsync("hashkey", "field", "value");

                var del1 = conn.HashDeleteAsync("hashkey", "field");
                var del2 = conn.HashDeleteAsync("hashkey", "field");

                Assert.False(del0.Result);
                Assert.True(del1.Result);
                Assert.False(del2.Result);
            }
        }

        [Fact]
        public void TestDelMulti() // https://redis.io/commands/hdel
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(3);
                conn.HashSetAsync("TestDelMulti", "key1", "val1");
                conn.HashSetAsync("TestDelMulti", "key2", "val2");
                conn.HashSetAsync("TestDelMulti", "key3", "val3");

                var s1 = conn.HashExistsAsync("TestDelMulti", "key1");
                var s2 = conn.HashExistsAsync("TestDelMulti", "key2");
                var s3 = conn.HashExistsAsync("TestDelMulti", "key3");

                var removed = conn.HashDeleteAsync("TestDelMulti", new RedisValue[] { "key1", "key3" });

                var d1 = conn.HashExistsAsync("TestDelMulti", "key1");
                var d2 = conn.HashExistsAsync("TestDelMulti", "key2");
                var d3 = conn.HashExistsAsync("TestDelMulti", "key3");

                Assert.True(conn.Wait(s1));
                Assert.True(conn.Wait(s2));
                Assert.True(conn.Wait(s3));

                Assert.Equal(2, conn.Wait(removed));

                Assert.False(conn.Wait(d1));
                Assert.True(conn.Wait(d2));
                Assert.False(conn.Wait(d3));

                var removeFinal = conn.HashDeleteAsync("TestDelMulti", new RedisValue[] { "key2" });

                Assert.Equal(0, conn.Wait(conn.HashLengthAsync("TestDelMulti")));
                Assert.Equal(1, conn.Wait(removeFinal));
            }
        }

        [Fact]
        public void TestDelMultiInsideTransaction() // https://redis.io/commands/hdel
        {
            using (var outer = GetUnsecuredConnection())
            {
                var conn = outer.GetDatabase(3).CreateTransaction();
                {
                    conn.HashSetAsync("TestDelMulti", "key1", "val1");
                    conn.HashSetAsync("TestDelMulti", "key2", "val2");
                    conn.HashSetAsync("TestDelMulti", "key3", "val3");

                    var s1 = conn.HashExistsAsync("TestDelMulti", "key1");
                    var s2 = conn.HashExistsAsync("TestDelMulti", "key2");
                    var s3 = conn.HashExistsAsync("TestDelMulti", "key3");

                    var removed = conn.HashDeleteAsync("TestDelMulti", new RedisValue[] { "key1", "key3" });

                    var d1 = conn.HashExistsAsync("TestDelMulti", "key1");
                    var d2 = conn.HashExistsAsync("TestDelMulti", "key2");
                    var d3 = conn.HashExistsAsync("TestDelMulti", "key3");

                    conn.Execute();

                    Assert.True(conn.Wait(s1));
                    Assert.True(conn.Wait(s2));
                    Assert.True(conn.Wait(s3));

                    Assert.Equal(2, conn.Wait(removed));

                    Assert.False(conn.Wait(d1));
                    Assert.True(conn.Wait(d2));
                    Assert.False(conn.Wait(d3));
                }
            }
        }

        [Fact]
        public void TestExists() // https://redis.io/commands/hexists
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(9);
                conn.KeyDeleteAsync("hashkey");
                var ex0 = conn.HashExistsAsync("hashkey", "field");
                conn.HashSetAsync("hashkey", "field", "value");
                var ex1 = conn.HashExistsAsync("hashkey", "field");
                conn.HashDeleteAsync("hashkey", "field");
                var ex2 = conn.HashExistsAsync("hashkey", "field");

                Assert.False(ex0.Result);
                Assert.True(ex1.Result);
                Assert.False(ex0.Result);
            }
        }

        [Fact]
        public void TestHashKeys() // https://redis.io/commands/hkeys
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(9);
                conn.KeyDeleteAsync("hashkey");

                var keys0 = conn.HashKeysAsync("hashkey");

                conn.HashSetAsync("hashkey", "foo", "abc");
                conn.HashSetAsync("hashkey", "bar", "def");

                var keys1 = conn.HashKeysAsync("hashkey");

                Assert.Empty(keys0.Result);

                var arr = keys1.Result;
                Assert.Equal(2, arr.Length);
                Assert.Equal("foo", arr[0]);
                Assert.Equal("bar", arr[1]);
            }
        }

        [Fact]
        public void TestHashValues() // https://redis.io/commands/hvals
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(9);
                conn.KeyDeleteAsync("hashkey");

                var keys0 = conn.HashValuesAsync("hashkey");

                conn.HashSetAsync("hashkey", "foo", "abc");
                conn.HashSetAsync("hashkey", "bar", "def");

                var keys1 = conn.HashValuesAsync("hashkey");

                Assert.Empty(keys0.Result);

                var arr = keys1.Result;
                Assert.Equal(2, arr.Length);
                Assert.Equal("abc", Encoding.UTF8.GetString(arr[0]));
                Assert.Equal("def", Encoding.UTF8.GetString(arr[1]));
            }
        }

        [Fact]
        public void TestHashLength() // https://redis.io/commands/hlen
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(9);
                conn.KeyDeleteAsync("hashkey");

                var len0 = conn.HashLengthAsync("hashkey");

                conn.HashSetAsync("hashkey", "foo", "abc");
                conn.HashSetAsync("hashkey", "bar", "def");

                var len1 = conn.HashLengthAsync("hashkey");

                Assert.Equal(0, len0.Result);
                Assert.Equal(2, len1.Result);
            }
        }

        [Fact]
        public void TestGetMulti() // https://redis.io/commands/hmget
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(9);
                conn.KeyDeleteAsync("hashkey");

                RedisValue[] fields = { "foo", "bar", "blop" };
                var result0 = conn.HashGetAsync("hashkey", fields);

                conn.HashSetAsync("hashkey", "foo", "abc");
                conn.HashSetAsync("hashkey", "bar", "def");

                var result1 = conn.HashGetAsync("hashkey", fields);

                var result2 = conn.HashGetAsync("hashkey", fields);

                var arr0 = result0.Result;
                var arr1 = result1.Result;
                var arr2 = result2.Result;

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
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(9);
                conn.KeyDeleteAsync("hashkey");

                var result0 = conn.HashGetAllAsync("hashkey");

                conn.HashSetAsync("hashkey", "foo", "abc");
                conn.HashSetAsync("hashkey", "bar", "def");

                var result1 = conn.HashGetAllAsync("hashkey");

                Assert.Empty(result0.Result);
                var result = result1.Result.ToStringDictionary();
                Assert.Equal(2, result.Count);
                Assert.Equal("abc", result["foo"]);
                Assert.Equal("def", result["bar"]);
            }
        }

        [Fact]
        public void TestSetPairs() // https://redis.io/commands/hmset
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(9);
                conn.KeyDeleteAsync("hashkey");

                var result0 = conn.HashGetAllAsync("hashkey");

                var data = new HashEntry[] {
                    new HashEntry("foo", Encoding.UTF8.GetBytes("abc")),
                    new HashEntry("bar", Encoding.UTF8.GetBytes("def"))
                };
                conn.HashSetAsync("hashkey", data);

                var result1 = conn.HashGetAllAsync("hashkey");

                Assert.Empty(result0.Result);
                var result = result1.Result.ToStringDictionary();
                Assert.Equal(2, result.Count);
                Assert.Equal("abc", result["foo"]);
                Assert.Equal("def", result["bar"]);
            }
        }
    }
}
