using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Databases : TestBase
    {
        public Databases(ITestOutputHelper output) : base (output) { }

        [Fact]
        public async Task CountKeys()
        {
            var db1Id = TestConfig.GetDedicatedDB();
            var db2Id = TestConfig.GetDedicatedDB();
            using (var muxer = Create(allowAdmin: true))
            {
                Skip.IfMissingDatabase(muxer, db1Id);
                Skip.IfMissingDatabase(muxer, db2Id);
                var server = GetAnyMaster(muxer);
                server.FlushDatabase(db1Id, CommandFlags.FireAndForget);
                server.FlushDatabase(db2Id, CommandFlags.FireAndForget);
            }
            using (var muxer = Create())
            {
                Skip.IfMissingDatabase(muxer, db1Id);
                Skip.IfMissingDatabase(muxer, db2Id);
                RedisKey key = Me();
                var db61 = muxer.GetDatabase(db1Id);
                var db62 = muxer.GetDatabase(db2Id);
                db61.StringSet("abc", "def", flags: CommandFlags.FireAndForget);
                db61.StringIncrement(key, flags: CommandFlags.FireAndForget);
                db62.StringIncrement(key, flags: CommandFlags.FireAndForget);

                var server = GetAnyMaster(muxer);
                var c0 = server.DatabaseSizeAsync(db1Id);
                var c1 = server.DatabaseSizeAsync(db2Id);

                Assert.Equal(2, await c0);
                Assert.Equal(1, await c1);
            }
        }

        [Fact]
        public void DatabaseCount()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var server = GetAnyMaster(muxer);
                var count = server.DatabaseCount;
                Log("Count: " + count);
                var configVal = server.ConfigGet("databases")[0].Value;
                Log("Config databases: " + configVal);
                Assert.Equal(int.Parse(configVal), count);
            }
        }

        [Fact]
        public void MultiDatabases()
        {
            using (var muxer = Create())
            {
                RedisKey key = Me();
                var db0 = muxer.GetDatabase(TestConfig.GetDedicatedDB(muxer));
                var db1 = muxer.GetDatabase(TestConfig.GetDedicatedDB(muxer));
                var db2 = muxer.GetDatabase(TestConfig.GetDedicatedDB(muxer));
                db0.Ping();

                db0.KeyDelete(key, CommandFlags.FireAndForget);
                db1.KeyDelete(key, CommandFlags.FireAndForget);
                db2.KeyDelete(key, CommandFlags.FireAndForget);

                muxer.WaitAll(
                    db0.StringSetAsync(key, "a"),
                    db1.StringSetAsync(key, "b"),
                    db2.StringSetAsync(key, "c")
                );

                var a = db0.StringGetAsync(key);
                var b = db1.StringGetAsync(key);
                var c = db2.StringGetAsync(key);
                muxer.WaitAll(a, b, c);

                Assert.Equal("a", muxer.Wait(a)); // db:0
                Assert.Equal("b", muxer.Wait(b)); // db:1
                Assert.Equal("c", muxer.Wait(c)); // db:2
            }
        }
    }
}
