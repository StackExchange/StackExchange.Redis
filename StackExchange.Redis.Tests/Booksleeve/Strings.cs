using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve
{
    public class Strings : BookSleeveTestBase // https://redis.io/commands#string
    {
        public Strings(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void Append()
        {
            using (var muxer = GetUnsecuredConnection(waitForOpen: true))
            {
                var conn = muxer.GetDatabase(2);
                var server = GetServer(muxer);
                var key = Me();
                conn.KeyDelete(key);
                var l0 = server.Features.StringLength ? conn.StringLengthAsync(key) : null;

                var s0 = conn.StringGetAsync(key);

                conn.StringSetAsync(key, "abc");
                var s1 = conn.StringGetAsync(key);
                var l1 = server.Features.StringLength ? conn.StringLengthAsync(key) : null;

                var result = conn.StringAppendAsync(key, Encode("defgh"));
                var s3 = conn.StringGetAsync(key);
                var l2 = server.Features.StringLength ? conn.StringLengthAsync(key) : null;

                Assert.Null((string)conn.Wait(s0));
                Assert.Equal("abc", conn.Wait(s1));
                Assert.Equal(8, conn.Wait(result));
                Assert.Equal("abcdefgh", conn.Wait(s3));

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
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(2);
                var key = Me();
                conn.KeyDeleteAsync(key);

                conn.StringSetAsync(key, "abc");
                var v1 = conn.StringGetAsync(key);

                conn.StringSetAsync(key, Encode("def"));
                var v2 = conn.StringGetAsync(key);

                Assert.Equal("abc", conn.Wait(v1));
                Assert.Equal("def", Decode(conn.Wait(v2)));
            }
        }

        [Fact]
        public void SetNotExists()
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(2);
                var prefix = Me();
                conn.KeyDeleteAsync(prefix + "1");
                conn.KeyDeleteAsync(prefix + "2");
                conn.KeyDeleteAsync(prefix + "3");
                conn.StringSetAsync(prefix + "1", "abc");

                var x0 = conn.StringSetAsync(prefix + "1", "def", when: When.NotExists);
                var x1 = conn.StringSetAsync(prefix + "1", Encode("def"), when: When.NotExists);
                var x2 = conn.StringSetAsync(prefix + "2", "def", when: When.NotExists);
                var x3 = conn.StringSetAsync(prefix + "3", Encode("def"), when: When.NotExists);

                var s0 = conn.StringGetAsync(prefix + "1");
                var s2 = conn.StringGetAsync(prefix + "2");
                var s3 = conn.StringGetAsync(prefix + "3");

                Assert.False(conn.Wait(x0));
                Assert.False(conn.Wait(x1));
                Assert.True(conn.Wait(x2));
                Assert.True(conn.Wait(x3));
                Assert.Equal("abc", conn.Wait(s0));
                Assert.Equal("def", conn.Wait(s2));
                Assert.Equal("def", conn.Wait(s3));
            }
        }

        [Fact]
        public void Ranges()
        {
            using (var muxer = GetUnsecuredConnection(waitForOpen: true))
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.StringSetRange), r => r.StringSetRange);
                var conn = muxer.GetDatabase(2);
                var key = Me();

                conn.KeyDeleteAsync(key);

                conn.StringSetAsync(key, "abcdefghi");
                conn.StringSetRangeAsync(key, 2, "xy");
                conn.StringSetRangeAsync(key, 4, Encode("z"));

                var val = conn.StringGetAsync(key);

                Assert.Equal("abxyzfghi", conn.Wait(val));
            }
        }

        [Fact]
        public void IncrDecr()
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(2);
                var key = Me();
                conn.KeyDeleteAsync(key);

                conn.StringSetAsync(key, "2");
                var v1 = conn.StringIncrementAsync(key);
                var v2 = conn.StringIncrementAsync(key, 5);
                var v3 = conn.StringIncrementAsync(key, -2);
                var v4 = conn.StringDecrementAsync(key);
                var v5 = conn.StringDecrementAsync(key, 5);
                var v6 = conn.StringDecrementAsync(key, -2);
                var s = conn.StringGetAsync(key);

                Assert.Equal(3, conn.Wait(v1));
                Assert.Equal(8, conn.Wait(v2));
                Assert.Equal(6, conn.Wait(v3));
                Assert.Equal(5, conn.Wait(v4));
                Assert.Equal(0, conn.Wait(v5));
                Assert.Equal(2, conn.Wait(v6));
                Assert.Equal("2", conn.Wait(s));
            }
        }

        [Fact]
        public void IncrDecrFloat()
        {
            using (var muxer = GetUnsecuredConnection(waitForOpen: true))
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.IncrementFloat), r => r.IncrementFloat);
                var conn = muxer.GetDatabase(2);
                var key = Me();
                conn.KeyDelete(key);

                conn.StringSetAsync(key, "2");
                var v1 = conn.StringIncrementAsync(key, 1.1);
                var v2 = conn.StringIncrementAsync(key, 5.0);
                var v3 = conn.StringIncrementAsync(key, -2.0);
                var v4 = conn.StringIncrementAsync(key, -1.0);
                var v5 = conn.StringIncrementAsync(key, -5.0);
                var v6 = conn.StringIncrementAsync(key, 2.0);

                var s = conn.StringGetAsync(key);

                Assert.Equal(3.1, conn.Wait(v1), 5);
                Assert.Equal(8.1, conn.Wait(v2), 5);
                Assert.Equal(6.1, conn.Wait(v3), 5);
                Assert.Equal(5.1, conn.Wait(v4), 5);
                Assert.Equal(0.1, conn.Wait(v5), 5);
                Assert.Equal(2.1, conn.Wait(v6), 5);
                Assert.Equal(2.1, (double)conn.Wait(s), 5);
            }
        }

        [Fact]
        public void GetRange()
        {
            using (var muxer = GetUnsecuredConnection(waitForOpen: true))
            {
                var conn = muxer.GetDatabase(2);
                var key = Me();
                conn.KeyDeleteAsync(key);

                conn.StringSetAsync(key, "abcdefghi");
                var s = conn.StringGetRangeAsync(key, 2, 4);
                var b = conn.StringGetRangeAsync(key, 2, 4);

                Assert.Equal("cde", conn.Wait(s));
                Assert.Equal("cde", Decode(conn.Wait(b)));
            }
        }

        [Fact]
        public void BitCount()
        {
            using (var muxer = GetUnsecuredConnection(waitForOpen: true))
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.BitwiseOperations), r => r.BitwiseOperations);

                var conn = muxer.GetDatabase(0);
                var key = Me();
                conn.StringSetAsync(key, "foobar");
                var r1 = conn.StringBitCountAsync(key);
                var r2 = conn.StringBitCountAsync(key, 0, 0);
                var r3 = conn.StringBitCountAsync(key, 1, 1);

                Assert.Equal(26, conn.Wait(r1));
                Assert.Equal(4, conn.Wait(r2));
                Assert.Equal(6, conn.Wait(r3));
            }
        }

        [Fact]
        public void BitOp()
        {
            using (var muxer = GetUnsecuredConnection(waitForOpen: true))
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.BitwiseOperations), r => r.BitwiseOperations);
                var conn = muxer.GetDatabase(0);
                var prefix = Me();
                var key1 = prefix + "1";
                var key2 = prefix + "2";
                var key3 = prefix + "3";
                conn.StringSetAsync(key1, new byte[] { 3 });
                conn.StringSetAsync(key2, new byte[] { 6 });
                conn.StringSetAsync(key3, new byte[] { 12 });

                var len_and = conn.StringBitOperationAsync(Bitwise.And, "and", new RedisKey[] { key1, key2, key3 });
                var len_or = conn.StringBitOperationAsync(Bitwise.Or, "or", new RedisKey[] { key1, key2, key3 });
                var len_xor = conn.StringBitOperationAsync(Bitwise.Xor, "xor", new RedisKey[] { key1, key2, key3 });
                var len_not = conn.StringBitOperationAsync(Bitwise.Not, "not", key1);

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
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(0);
                var key = Me();
                conn.StringSetAsync(key, "hello world");
                var result = conn.StringGetRangeAsync(key, 2, 6);
                Assert.Equal("llo w", conn.Wait(result));
            }
        }

        private static byte[] Encode(string value) => Encoding.UTF8.GetBytes(value);
        private static string Decode(byte[] value) => Encoding.UTF8.GetString(value);
    }
}
