using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Strings : TestBase // https://redis.io/commands#string
    {
        public Strings(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public async Task Append()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var server = GetServer(muxer);
                var key = Me();
                conn.KeyDelete(key, CommandFlags.FireAndForget);
                var l0 = server.Features.StringLength ? conn.StringLengthAsync(key) : null;

                var s0 = conn.StringGetAsync(key);

                conn.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
                var s1 = conn.StringGetAsync(key);
                var l1 = server.Features.StringLength ? conn.StringLengthAsync(key) : null;

                var result = conn.StringAppendAsync(key, Encode("defgh"));
                var s3 = conn.StringGetAsync(key);
                var l2 = server.Features.StringLength ? conn.StringLengthAsync(key) : null;

                Assert.Null((string)await s0);
                Assert.Equal("abc", await s1);
                Assert.Equal(8, await result);
                Assert.Equal("abcdefgh", await s3);

                if (server.Features.StringLength)
                {
                    Assert.Equal(0, await l0);
                    Assert.Equal(3, await l1);
                    Assert.Equal(8, await l2);
                }
            }
        }

        [Fact]
        public async Task Set()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.KeyDelete(key, CommandFlags.FireAndForget);

                conn.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
                var v1 = conn.StringGetAsync(key);

                conn.StringSet(key, Encode("def"), flags: CommandFlags.FireAndForget);
                var v2 = conn.StringGetAsync(key);

                Assert.Equal("abc", await v1);
                Assert.Equal("def", Decode(await v2));
            }
        }

        [Fact]
        public async Task GetLease()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.KeyDelete(key, CommandFlags.FireAndForget);

                conn.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
                using (var v1 = await conn.StringGetLeaseAsync(key).ConfigureAwait(false))
                {
                    string s = v1.DecodeString();
                    Assert.Equal("abc", s);
                }
            }
        }

        [Fact]
        public async Task GetLeaseAsStream()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.KeyDelete(key, CommandFlags.FireAndForget);

                conn.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
                using (var v1 = (await conn.StringGetLeaseAsync(key).ConfigureAwait(false)).AsStream())
                {
                    using (var sr = new StreamReader(v1))
                    {
                        string s = sr.ReadToEnd();
                        Assert.Equal("abc", s);
                    }
                }
            }
        }

        [Fact]
        public async Task SetNotExists()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var prefix = Me();
                conn.KeyDelete(prefix + "1", CommandFlags.FireAndForget);
                conn.KeyDelete(prefix + "2", CommandFlags.FireAndForget);
                conn.KeyDelete(prefix + "3", CommandFlags.FireAndForget);
                conn.StringSet(prefix + "1", "abc", flags: CommandFlags.FireAndForget);

                var x0 = conn.StringSetAsync(prefix + "1", "def", when: When.NotExists);
                var x1 = conn.StringSetAsync(prefix + "1", Encode("def"), when: When.NotExists);
                var x2 = conn.StringSetAsync(prefix + "2", "def", when: When.NotExists);
                var x3 = conn.StringSetAsync(prefix + "3", Encode("def"), when: When.NotExists);

                var s0 = conn.StringGetAsync(prefix + "1");
                var s2 = conn.StringGetAsync(prefix + "2");
                var s3 = conn.StringGetAsync(prefix + "3");

                Assert.False(await x0);
                Assert.False(await x1);
                Assert.True(await x2);
                Assert.True(await x3);
                Assert.Equal("abc", await s0);
                Assert.Equal("def", await s2);
                Assert.Equal("def", await s3);
            }
        }

        [Fact]
        public async Task Ranges()
        {
            using (var muxer = Create())
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.StringSetRange), r => r.StringSetRange);
                var conn = muxer.GetDatabase();
                var key = Me();

                conn.KeyDelete(key, CommandFlags.FireAndForget);

                conn.StringSet(key, "abcdefghi", flags: CommandFlags.FireAndForget);
                conn.StringSetRange(key, 2, "xy", CommandFlags.FireAndForget);
                conn.StringSetRange(key, 4, Encode("z"), CommandFlags.FireAndForget);

                var val = conn.StringGetAsync(key);

                Assert.Equal("abxyzfghi", await val);
            }
        }

        [Fact]
        public async Task IncrDecr()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.KeyDelete(key, CommandFlags.FireAndForget);

                conn.StringSet(key, "2", flags: CommandFlags.FireAndForget);
                var v1 = conn.StringIncrementAsync(key);
                var v2 = conn.StringIncrementAsync(key, 5);
                var v3 = conn.StringIncrementAsync(key, -2);
                var v4 = conn.StringDecrementAsync(key);
                var v5 = conn.StringDecrementAsync(key, 5);
                var v6 = conn.StringDecrementAsync(key, -2);
                var s = conn.StringGetAsync(key);

                Assert.Equal(3, await v1);
                Assert.Equal(8, await v2);
                Assert.Equal(6, await v3);
                Assert.Equal(5, await v4);
                Assert.Equal(0, await v5);
                Assert.Equal(2, await v6);
                Assert.Equal("2", await s);
            }
        }

        [Fact]
        public async Task IncrDecrFloat()
        {
            using (var muxer = Create())
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.IncrementFloat), r => r.IncrementFloat);
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.KeyDelete(key, CommandFlags.FireAndForget);

                conn.StringSet(key, "2", flags: CommandFlags.FireAndForget);
                var v1 = conn.StringIncrementAsync(key, 1.1);
                var v2 = conn.StringIncrementAsync(key, 5.0);
                var v3 = conn.StringIncrementAsync(key, -2.0);
                var v4 = conn.StringIncrementAsync(key, -1.0);
                var v5 = conn.StringIncrementAsync(key, -5.0);
                var v6 = conn.StringIncrementAsync(key, 2.0);

                var s = conn.StringGetAsync(key);

                Assert.Equal(3.1, await v1, 5);
                Assert.Equal(8.1, await v2, 5);
                Assert.Equal(6.1, await v3, 5);
                Assert.Equal(5.1, await v4, 5);
                Assert.Equal(0.1, await v5, 5);
                Assert.Equal(2.1, await v6, 5);
                Assert.Equal(2.1, (double)await s, 5);
            }
        }

        [Fact]
        public async Task GetRange()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.KeyDelete(key, CommandFlags.FireAndForget);

                conn.StringSet(key, "abcdefghi", flags: CommandFlags.FireAndForget);
                var s = conn.StringGetRangeAsync(key, 2, 4);
                var b = conn.StringGetRangeAsync(key, 2, 4);

                Assert.Equal("cde", await s);
                Assert.Equal("cde", Decode(await b));
            }
        }

        [Fact]
        public async Task BitCount()
        {
            using (var muxer = Create())
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.BitwiseOperations), r => r.BitwiseOperations);

                var conn = muxer.GetDatabase();
                var key = Me();
                conn.StringSet(key, "foobar", flags: CommandFlags.FireAndForget);
                var r1 = conn.StringBitCountAsync(key);
                var r2 = conn.StringBitCountAsync(key, 0, 0);
                var r3 = conn.StringBitCountAsync(key, 1, 1);

                Assert.Equal(26, await r1);
                Assert.Equal(4, await r2);
                Assert.Equal(6, await r3);
            }
        }

        [Fact]
        public async Task BitOp()
        {
            using (var muxer = Create())
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.BitwiseOperations), r => r.BitwiseOperations);
                var conn = muxer.GetDatabase();
                var prefix = Me();
                var key1 = prefix + "1";
                var key2 = prefix + "2";
                var key3 = prefix + "3";
                conn.StringSet(key1, new byte[] { 3 }, flags: CommandFlags.FireAndForget);
                conn.StringSet(key2, new byte[] { 6 }, flags: CommandFlags.FireAndForget);
                conn.StringSet(key3, new byte[] { 12 }, flags: CommandFlags.FireAndForget);

                var len_and = conn.StringBitOperationAsync(Bitwise.And, "and", new RedisKey[] { key1, key2, key3 });
                var len_or = conn.StringBitOperationAsync(Bitwise.Or, "or", new RedisKey[] { key1, key2, key3 });
                var len_xor = conn.StringBitOperationAsync(Bitwise.Xor, "xor", new RedisKey[] { key1, key2, key3 });
                var len_not = conn.StringBitOperationAsync(Bitwise.Not, "not", key1);

                Assert.Equal(1, await len_and);
                Assert.Equal(1, await len_or);
                Assert.Equal(1, await len_xor);
                Assert.Equal(1, await len_not);

                var r_and = ((byte[])(await conn.StringGetAsync("and").ForAwait())).Single();
                var r_or = ((byte[])(await conn.StringGetAsync("or").ForAwait())).Single();
                var r_xor = ((byte[])(await conn.StringGetAsync("xor").ForAwait())).Single();
                var r_not = ((byte[])(await conn.StringGetAsync("not").ForAwait())).Single();

                Assert.Equal((byte)(3 & 6 & 12), r_and);
                Assert.Equal((byte)(3 | 6 | 12), r_or);
                Assert.Equal((byte)(3 ^ 6 ^ 12), r_xor);
                Assert.Equal(unchecked((byte)(~3)), r_not);
            }
        }

        [Fact]
        public async Task RangeString()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.StringSet(key, "hello world", flags: CommandFlags.FireAndForget);
                var result = conn.StringGetRangeAsync(key, 2, 6);
                Assert.Equal("llo w", await result);
            }
        }

        [Fact]
        public async Task HashStringLengthAsync()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.HashStringLength), r => r.HashStringLength);
                var database = conn.GetDatabase();
                var key = Me();
                var value = "hello world";
                database.HashSet(key, "field", value);
                var resAsync = database.HashStringLengthAsync(key, "field");
                var resNonExistingAsync = database.HashStringLengthAsync(key, "non-existing-field");
                Assert.Equal(value.Length, await resAsync);
                Assert.Equal(0, await resNonExistingAsync);
            }
        }

        [Fact]
        public void HashStringLength()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.HashStringLength), r => r.HashStringLength);
                var database = conn.GetDatabase();
                var key = Me();
                var value = "hello world";
                database.HashSet(key, "field", value);
                Assert.Equal(value.Length, database.HashStringLength(key, "field"));
                Assert.Equal(0, database.HashStringLength(key, "non-existing-field"));
            }
        }

        private static byte[] Encode(string value) => Encoding.UTF8.GetBytes(value);
        private static string Decode(byte[] value) => Encoding.UTF8.GetString(value);
    }
}
