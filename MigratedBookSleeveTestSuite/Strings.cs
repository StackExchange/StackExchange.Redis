using System.Linq;
using System.Text;
using StackExchange.Redis;
using Xunit;

namespace Tests
{
    public class Strings // http://redis.io/commands#string
    {
        [Fact]
        public void Append()
        {
            using (var muxer = Config.GetUnsecuredConnection(waitForOpen: true))
            {
                var conn = muxer.GetDatabase(2);
                var server = Config.GetServer(muxer);
                conn.KeyDelete("append");
                var l0 = server.Features.StringLength ? conn.StringLengthAsync("append") : null;

                var s0 = conn.StringGetAsync("append");

                conn.StringSetAsync("append", "abc");
                var s1 = conn.StringGetAsync("append");
                var l1 = server.Features.StringLength ? conn.StringLengthAsync("append") : null;

                var result = conn.StringAppendAsync("append", Encode("defgh"));
                var s3 = conn.StringGetAsync("append");
                var l2 = server.Features.StringLength ? conn.StringLengthAsync("append") : null;

                Assert.Null((string)conn.Wait(s0));
                Assert.Equal("abc", (string)conn.Wait(s1));
                Assert.Equal(8, conn.Wait(result));
                Assert.Equal("abcdefgh", (string)conn.Wait(s3));

                if (server.Features.StringLength)
                {
                    Assert.Equal(0, conn.Wait(l0));
                    Assert.Equal(3, conn.Wait(l1));
                    Assert.Equal(8, conn.Wait(l2));
                }
            }
        }
        [Fact]
        public void Set()
        {
            using (var muxer = Config.GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(2);
                conn.KeyDeleteAsync("set");

                conn.StringSetAsync("set", "abc");
                var v1 = conn.StringGetAsync("set");

                conn.StringSetAsync("set", Encode("def"));
                var v2 = conn.StringGetAsync("set");

                Assert.Equal("abc", (string)conn.Wait(v1));
                Assert.Equal("def", (string)Decode(conn.Wait(v2)));
            }
        }

        [Fact]
        public void SetNotExists()
        {
            using (var muxer = Config.GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(2);
                conn.KeyDeleteAsync("set");
                conn.KeyDeleteAsync("set2");
                conn.KeyDeleteAsync("set3");
                conn.StringSetAsync("set", "abc");

                var x0 = conn.StringSetAsync("set", "def", when: When.NotExists);
                var x1 = conn.StringSetAsync("set", Encode("def"), when: When.NotExists);
                var x2 = conn.StringSetAsync("set2", "def", when: When.NotExists);
                var x3 = conn.StringSetAsync("set3", Encode("def"), when: When.NotExists);

                var s0 = conn.StringGetAsync("set");
                var s2 = conn.StringGetAsync("set2");
                var s3 = conn.StringGetAsync("set3");

                Assert.False(conn.Wait(x0));
                Assert.False(conn.Wait(x1));
                Assert.True(conn.Wait(x2));
                Assert.True(conn.Wait(x3));
                Assert.Equal("abc", (string)conn.Wait(s0));
                Assert.Equal("def", (string)conn.Wait(s2));
                Assert.Equal("def", (string)conn.Wait(s3));
            }
        }

        [Fact]
        public void Ranges()
        {
            using (var muxer = Config.GetUnsecuredConnection(waitForOpen: true))
            {
                if (!Config.GetFeatures(muxer).StringSetRange)
                {
                    Skip.NotSupported(nameof(RedisFeatures.StringSetRange));
                }
                var conn = muxer.GetDatabase(2);

                conn.KeyDeleteAsync("range");

                conn.StringSetAsync("range", "abcdefghi");
                conn.StringSetRangeAsync("range", 2, "xy");
                conn.StringSetRangeAsync("range", 4, Encode("z"));

                var val = conn.StringGetAsync("range");

                Assert.Equal("abxyzfghi", (string)conn.Wait(val));
            }
        }

        [Fact]
        public void IncrDecr()
        {
            using (var muxer = Config.GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(2);
                conn.KeyDeleteAsync("incr");

                conn.StringSetAsync("incr", "2");
                var v1 = conn.StringIncrementAsync("incr");
                var v2 = conn.StringIncrementAsync("incr", 5);
                var v3 = conn.StringIncrementAsync("incr", -2);
                var v4 = conn.StringDecrementAsync("incr");
                var v5 = conn.StringDecrementAsync("incr", 5);
                var v6 = conn.StringDecrementAsync("incr", -2);
                var s = conn.StringGetAsync("incr");

                Assert.Equal(3, conn.Wait(v1));
                Assert.Equal(8, conn.Wait(v2));
                Assert.Equal(6, conn.Wait(v3));
                Assert.Equal(5, conn.Wait(v4));
                Assert.Equal(0, conn.Wait(v5));
                Assert.Equal(2, conn.Wait(v6));
                Assert.Equal("2", (string)conn.Wait(s));
            }
        }

        [SkippableFact]
        public void IncrDecrFloat()
        {
            using (var muxer = Config.GetUnsecuredConnection(waitForOpen: true))
            {
                if (!Config.GetFeatures(muxer).IncrementFloat)
                {
                    Skip.NotSupported(nameof(RedisFeatures.IncrementFloat));
                }
                var conn = muxer.GetDatabase(2);
                conn.KeyDelete("incr");

                conn.StringSetAsync("incr", "2");
                var v1 = conn.StringIncrementAsync("incr", 1.1);
                var v2 = conn.StringIncrementAsync("incr", 5.0);
                var v3 = conn.StringIncrementAsync("incr", -2.0);
                var v4 = conn.StringIncrementAsync("incr", -1.0);
                var v5 = conn.StringIncrementAsync("incr", -5.0);
                var v6 = conn.StringIncrementAsync("incr", 2.0);

                var s = conn.StringGetAsync("incr");

                Config.AssertNearlyEqual(3.1, conn.Wait(v1));
                Config.AssertNearlyEqual(8.1, conn.Wait(v2));
                Config.AssertNearlyEqual(6.1, conn.Wait(v3));
                Config.AssertNearlyEqual(5.1, conn.Wait(v4));
                Config.AssertNearlyEqual(0.1, conn.Wait(v5));
                Config.AssertNearlyEqual(2.1, conn.Wait(v6));
                Assert.Equal("2.1", (string)conn.Wait(s));
            }
        }

        [Fact]
        public void GetRange()
        {
            using (var muxer = Config.GetUnsecuredConnection(waitForOpen: true))
            {
                var conn = muxer.GetDatabase(2);
                conn.KeyDeleteAsync("range");

                conn.StringSetAsync("range", "abcdefghi");
                var s = conn.StringGetRangeAsync("range", 2, 4);
                var b = conn.StringGetRangeAsync("range", 2, 4);

                Assert.Equal("cde", (string)conn.Wait(s));
                Assert.Equal("cde", Decode(conn.Wait(b)));
            }
        }

        [SkippableFact]
        public void BitCount()
        {
            using (var muxer = Config.GetUnsecuredConnection(waitForOpen: true))
            {
                if (!Config.GetFeatures(muxer).BitwiseOperations)
                {
                    Skip.NotSupported(nameof(RedisFeatures.BitwiseOperations));
                }

                var conn = muxer.GetDatabase(0);
                conn.StringSetAsync("mykey", "foobar");
                var r1 = conn.StringBitCountAsync("mykey");
                var r2 = conn.StringBitCountAsync("mykey", 0, 0);
                var r3 = conn.StringBitCountAsync("mykey", 1, 1);

                Assert.Equal(26, conn.Wait(r1));
                Assert.Equal(4, conn.Wait(r2));
                Assert.Equal(6, conn.Wait(r3));
            }
        }

        [SkippableFact]
        public void BitOp()
        {
            using (var muxer = Config.GetUnsecuredConnection(waitForOpen: true))
            {
                if (!Config.GetFeatures(muxer).BitwiseOperations)
                {
                    Skip.NotSupported(nameof(RedisFeatures.BitwiseOperations));
                }
                var conn = muxer.GetDatabase(0);
                conn.StringSetAsync("key1", new byte[] { 3 });
                conn.StringSetAsync("key2", new byte[] { 6 });
                conn.StringSetAsync("key3", new byte[] { 12 });

                var len_and = conn.StringBitOperationAsync(Bitwise.And, "and", new RedisKey[] { "key1", "key2", "key3" });
                var len_or = conn.StringBitOperationAsync(Bitwise.Or, "or", new RedisKey[] { "key1", "key2", "key3" });
                var len_xor = conn.StringBitOperationAsync(Bitwise.Xor, "xor", new RedisKey[] { "key1", "key2", "key3" });
                var len_not = conn.StringBitOperationAsync(Bitwise.Not, "not", "key1");

                Assert.Equal(1, conn.Wait(len_and));
                Assert.Equal(1, conn.Wait(len_or));
                Assert.Equal(1, conn.Wait(len_xor));
                Assert.Equal(1, conn.Wait(len_not));

                var r_and = ((byte[])conn.Wait(conn.StringGetAsync("and"))).Single();
                var r_or = ((byte[])conn.Wait(conn.StringGetAsync("or"))).Single();
                var r_xor = ((byte[])conn.Wait(conn.StringGetAsync("xor"))).Single();
                var r_not = ((byte[])conn.Wait(conn.StringGetAsync("not"))).Single();

                Assert.Equal((byte)(3 & 6 & 12), r_and);
                Assert.Equal((byte)(3 | 6 | 12), r_or);
                Assert.Equal((byte)(3 ^ 6 ^ 12), r_xor);
                Assert.Equal(unchecked((byte)(~3)), r_not);
            }
        }

        [Fact]
        public void RangeString()
        {
            using (var muxer = Config.GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(0);
                conn.StringSetAsync("my key", "hello world");
                var result = conn.StringGetRangeAsync("my key", 2, 6);
                Assert.Equal("llo w", (string)conn.Wait(result));
            }
        }
        static byte[] Encode(string value) { return Encoding.UTF8.GetBytes(value); }
        static string Decode(byte[] value) { return Encoding.UTF8.GetString(value); }
    }
}
