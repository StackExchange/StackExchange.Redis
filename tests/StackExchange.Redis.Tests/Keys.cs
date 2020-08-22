using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Keys : TestBase
    {
        public Keys(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

        [Fact]
        public void TestScan()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var dbId = TestConfig.GetDedicatedDB();
                var db = muxer.GetDatabase(dbId);
                var server = GetAnyMaster(muxer);
                var prefix = Me();
                server.FlushDatabase(dbId, flags: CommandFlags.FireAndForget);

                const int Count = 1000;
                for (int i = 0; i < Count; i++)
                    db.StringSet(prefix + "x" + i, "y" + i, flags: CommandFlags.FireAndForget);

                var count = server.Keys(dbId, prefix + "*").Count();
                Assert.Equal(Count, count);
            }
        }

        [Fact]
        public void FlushFetchRandomKey()
        {
            using (var conn = Create(allowAdmin: true))
            {
                var dbId = TestConfig.GetDedicatedDB(conn);
                Skip.IfMissingDatabase(conn, dbId);
                var db = conn.GetDatabase(dbId);
                var prefix = Me();
                conn.GetServer(TestConfig.Current.MasterServerAndPort).FlushDatabase(dbId, CommandFlags.FireAndForget);
                string anyKey = db.KeyRandom();

                Assert.Null(anyKey);
                db.StringSet(prefix + "abc", "def");
                byte[] keyBytes = db.KeyRandom();

                Assert.Equal(prefix + "abc", Encoding.UTF8.GetString(keyBytes));
            }
        }

        [Fact]
        public void Zeros()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                var key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringSet(key, 123, flags: CommandFlags.FireAndForget);
                int k = (int)db.StringGet(key);
                Assert.Equal(123, k);

                db.KeyDelete(key, CommandFlags.FireAndForget);
                int i = (int)db.StringGet(key);
                Assert.Equal(0, i);

                Assert.True(db.StringGet(key).IsNull);
                int? value = (int?)db.StringGet(key);
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
                Assert.Equal("helloworld", ret);
            }

            {
                RedisKey key1 = "world";
                RedisKey key2 = Encoding.UTF8.GetBytes("hello");
                var key3 = key1.Prepend(key2);
                Assert.True(ReferenceEquals(key1.KeyValue, key3.KeyValue));
                Assert.True(ReferenceEquals(key2.KeyValue, key3.KeyPrefix));
                Assert.Equal("helloworld", key3);
            }

            {
                RedisKey key = "hello";
                var ret = key.Append("world");
                Assert.Equal("helloworld", ret);
            }

            {
                RedisKey key1 = Encoding.UTF8.GetBytes("hello");
                RedisKey key2 = "world";
                var key3 = key1.Append(key2);
                Assert.True(ReferenceEquals(key2.KeyValue, key3.KeyValue));
                Assert.True(ReferenceEquals(key1.KeyValue, key3.KeyPrefix));
                Assert.Equal("helloworld", key3);
            }
        }

        [Fact]
        public void Exists()
        {
            using (var muxer = Create())
            {
                RedisKey key = Me();
                RedisKey key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);

                Assert.False(db.KeyExists(key));
                Assert.False(db.KeyExists(key2));
                Assert.Equal(0, db.KeyExists(new[] { key, key2 }));

                db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
                Assert.True(db.KeyExists(key));
                Assert.False(db.KeyExists(key2));
                Assert.Equal(1, db.KeyExists(new[] { key, key2 }));

                db.StringSet(key2, "new value", flags: CommandFlags.FireAndForget);
                Assert.True(db.KeyExists(key));
                Assert.True(db.KeyExists(key2));
                Assert.Equal(2, db.KeyExists(new[] { key, key2 }));
            }
        }

        [Fact]
        public async Task ExistsAsync()
        {
            using (var muxer = Create())
            {
                RedisKey key = Me();
                RedisKey key2 = Me() + "2";
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.KeyDelete(key2, CommandFlags.FireAndForget);
                var a1 = db.KeyExistsAsync(key).ForAwait();
                var a2 = db.KeyExistsAsync(key2).ForAwait();
                var a3 = db.KeyExistsAsync(new[] { key, key2 }).ForAwait();

                db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);

                var b1 = db.KeyExistsAsync(key).ForAwait();
                var b2 = db.KeyExistsAsync(key2).ForAwait();
                var b3 = db.KeyExistsAsync(new[] { key, key2 }).ForAwait();

                db.StringSet(key2, "new value", flags: CommandFlags.FireAndForget);

                var c1 = db.KeyExistsAsync(key).ForAwait();
                var c2 = db.KeyExistsAsync(key2).ForAwait();
                var c3 = db.KeyExistsAsync(new[] { key, key2 }).ForAwait();

                Assert.False(await a1);
                Assert.False(await a2);
                Assert.Equal(0, await a3);

                Assert.True(await b1);
                Assert.False(await b2);
                Assert.Equal(1, await b3);

                Assert.True(await c1);
                Assert.True(await c2);
                Assert.Equal(2, await c3);
            }
        }

        [Fact]
        public async Task IdleTime()
        {
            using (var muxer = Create())
            {
                RedisKey key = Me();
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
                await Task.Delay(2000).ForAwait();
                var idleTime = db.KeyIdleTime(key);
                Assert.True(idleTime > TimeSpan.Zero);

                db.StringSet(key, "new value2", flags: CommandFlags.FireAndForget);
                var idleTime2 = db.KeyIdleTime(key);
                Assert.True(idleTime2 < idleTime);

                db.KeyDelete(key);
                var idleTime3 = db.KeyIdleTime(key);
                Assert.Null(idleTime3);
            }
        }

        [Fact]
        public async Task TouchIdleTime()
        {
            using (var muxer = Create())
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.KeyTouch), r => r.KeyTouch);

                RedisKey key = Me();
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
                await Task.Delay(2000).ForAwait();
                var idleTime = db.KeyIdleTime(key);
                Assert.True(idleTime > TimeSpan.Zero);

                Assert.True(db.KeyTouch(key));
                var idleTime1 = db.KeyIdleTime(key);
                Assert.True(idleTime1 < idleTime);
            }
        }

        [Fact]
        public async Task IdleTimeAsync()
        {
            using (var muxer = Create())
            {
                RedisKey key = Me();
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
                await Task.Delay(2000).ForAwait();
                var idleTime = await db.KeyIdleTimeAsync(key).ForAwait();
                Assert.True(idleTime > TimeSpan.Zero);

                db.StringSet(key, "new value2", flags: CommandFlags.FireAndForget);
                var idleTime2 = await db.KeyIdleTimeAsync(key).ForAwait();
                Assert.True(idleTime2 < idleTime);

                db.KeyDelete(key);
                var idleTime3 = await db.KeyIdleTimeAsync(key).ForAwait();
                Assert.Null(idleTime3);
            }
        }

        [Fact]
        public async Task TouchIdleTimeAsync()
        {
            using (var muxer = Create())
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.KeyTouch), r => r.KeyTouch);

                RedisKey key = Me();
                var db = muxer.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
                await Task.Delay(2000).ForAwait();
                var idleTime = await db.KeyIdleTimeAsync(key).ForAwait();
                Assert.True(idleTime > TimeSpan.Zero);

                Assert.True(await db.KeyTouchAsync(key).ForAwait());
                var idleTime1 = await db.KeyIdleTimeAsync(key).ForAwait();
                Assert.True(idleTime1 < idleTime);
            }
        }
    }
}
