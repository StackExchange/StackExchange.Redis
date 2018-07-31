using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Keys : TestBase
    {
        public Keys(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void TestScan()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                const int Database = 43;
                var db = muxer.GetDatabase(Database);
                var server = GetAnyMaster(muxer);
                server.FlushDatabase(flags: CommandFlags.FireAndForget);

                const int Count = 1000;
                for (int i = 0; i < Count; i++)
                    db.StringSet("x" + i, "y" + i, flags: CommandFlags.FireAndForget);

                var count = server.Keys(Database).Count();
                Assert.Equal(Count, count);
            }
        }

        [Fact]
        public void RandomKey()
        {
            using (var conn = Create(allowAdmin: true))
            {
                var db = conn.GetDatabase();
                conn.GetServer(TestConfig.Current.MasterServerAndPort).FlushDatabase();
                string anyKey = db.KeyRandom();

                Assert.Null(anyKey);
                db.StringSet("abc", "def");
                byte[] keyBytes = db.KeyRandom();

                Assert.Equal("abc", Encoding.UTF8.GetString(keyBytes));
            }
        }

        [Fact]
        public void Zeros()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                db.KeyDelete("abc");
                db.StringSet("abc", 123);
                int k = (int)db.StringGet("abc");
                Assert.Equal(123, k);

                db.KeyDelete("abc");
                int i = (int)db.StringGet("abc");
                Assert.Equal(0, i);

                Assert.True(db.StringGet("abc").IsNull);
                int? value = (int?)db.StringGet("abc");
                Assert.False(value.HasValue);
            }
        }

        [Fact]
        public void PrependAppend()
        {
            {
                // simple
                RedisKey key = "world";
                var ret = key.Prepend("hello");
                Assert.Equal("helloworld", (string)ret);
            }

            {
                RedisKey key1 = "world";
                RedisKey key2 = Encoding.UTF8.GetBytes("hello");
                var key3 = key1.Prepend(key2);
                Assert.True(object.ReferenceEquals(key1.KeyValue, key3.KeyValue));
                Assert.True(object.ReferenceEquals(key2.KeyValue, key3.KeyPrefix));
                Assert.Equal("helloworld", (string)key3);
            }

            {
                RedisKey key = "hello";
                var ret = key.Append("world");
                Assert.Equal("helloworld", (string)ret);
            }

            {
                RedisKey key1 = Encoding.UTF8.GetBytes("hello");
                RedisKey key2 = "world";
                var key3 = key1.Append(key2);
                Assert.True(object.ReferenceEquals(key2.KeyValue, key3.KeyValue));
                Assert.True(object.ReferenceEquals(key1.KeyValue, key3.KeyPrefix));
                Assert.Equal("helloworld", (string)key3);
            }
        }
    }
}
