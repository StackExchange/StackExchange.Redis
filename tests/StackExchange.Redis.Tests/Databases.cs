using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Databases : TestBase
    {
        public Databases(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

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
            using (var muxer = Create(defaultDatabase: db2Id))
            {
                Skip.IfMissingDatabase(muxer, db1Id);
                Skip.IfMissingDatabase(muxer, db2Id);
                RedisKey key = Me();
                var dba = muxer.GetDatabase(db1Id);
                var dbb = muxer.GetDatabase(db2Id);
                dba.StringSet("abc", "def", flags: CommandFlags.FireAndForget);
                dba.StringIncrement(key, flags: CommandFlags.FireAndForget);
                dbb.StringIncrement(key, flags: CommandFlags.FireAndForget);

                var server = GetAnyMaster(muxer);
                var c0 = server.DatabaseSizeAsync(db1Id);
                var c1 = server.DatabaseSizeAsync(db2Id);
                var c2 = server.DatabaseSizeAsync(); // using default DB, which is db2Id

                Assert.Equal(2, await c0);
                Assert.Equal(1, await c1);
                Assert.Equal(1, await c2);
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
        public async Task MultiDatabases()
        {
            using (var muxer = Create())
            {
                RedisKey key = Me();
                var db0 = muxer.GetDatabase(TestConfig.GetDedicatedDB(muxer));
                var db1 = muxer.GetDatabase(TestConfig.GetDedicatedDB(muxer));
                var db2 = muxer.GetDatabase(TestConfig.GetDedicatedDB(muxer));

                db0.KeyDelete(key, CommandFlags.FireAndForget);
                db1.KeyDelete(key, CommandFlags.FireAndForget);
                db2.KeyDelete(key, CommandFlags.FireAndForget);

                db0.StringSet(key, "a", flags: CommandFlags.FireAndForget);
                db1.StringSet(key, "b", flags: CommandFlags.FireAndForget);
                db2.StringSet(key, "c", flags: CommandFlags.FireAndForget);

                var a = db0.StringGetAsync(key);
                var b = db1.StringGetAsync(key);
                var c = db2.StringGetAsync(key);

                Assert.Equal("a", await a); // db:0
                Assert.Equal("b", await b); // db:1
                Assert.Equal("c", await c); // db:2
            }
        }

        [Fact]
        public async Task SwapDatabases()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.SwapDB), r => r.SwapDB);

                RedisKey key = Me();
                var db0id = TestConfig.GetDedicatedDB(muxer);
                var db0 = muxer.GetDatabase(db0id);
                var db1id = TestConfig.GetDedicatedDB(muxer);
                var db1 = muxer.GetDatabase(db1id);

                db0.KeyDelete(key, CommandFlags.FireAndForget);
                db1.KeyDelete(key, CommandFlags.FireAndForget);

                db0.StringSet(key, "a", flags: CommandFlags.FireAndForget);
                db1.StringSet(key, "b", flags: CommandFlags.FireAndForget);

                var a = db0.StringGetAsync(key);
                var b = db1.StringGetAsync(key);

                Assert.Equal("a", await a); // db:0
                Assert.Equal("b", await b); // db:1

                var server = GetServer(muxer);
                server.SwapDatabases(db0id, db1id);

                var aNew = db1.StringGetAsync(key);
                var bNew = db0.StringGetAsync(key);

                Assert.Equal("a", await aNew); // db:1
                Assert.Equal("b", await bNew); // db:0
            }
        }

        [Fact]
        public async Task SwapDatabasesAsync()
        {
            using (var muxer = Create(allowAdmin: true))
            {
                Skip.IfMissingFeature(muxer, nameof(RedisFeatures.SwapDB), r => r.SwapDB);

                RedisKey key = Me();
                var db0id = TestConfig.GetDedicatedDB(muxer);
                var db0 = muxer.GetDatabase(db0id);
                var db1id = TestConfig.GetDedicatedDB(muxer);
                var db1 = muxer.GetDatabase(db1id);

                db0.KeyDelete(key, CommandFlags.FireAndForget);
                db1.KeyDelete(key, CommandFlags.FireAndForget);

                db0.StringSet(key, "a", flags: CommandFlags.FireAndForget);
                db1.StringSet(key, "b", flags: CommandFlags.FireAndForget);

                var a = db0.StringGetAsync(key);
                var b = db1.StringGetAsync(key);

                Assert.Equal("a", await a); // db:0
                Assert.Equal("b", await b); // db:1

                var server = GetServer(muxer);
                _ = server.SwapDatabasesAsync(db0id, db1id).ForAwait();

                var aNew = db1.StringGetAsync(key);
                var bNew = db0.StringGetAsync(key);

                Assert.Equal("a", await aNew); // db:1
                Assert.Equal("b", await bNew); // db:0
            }
        }
    }
}
