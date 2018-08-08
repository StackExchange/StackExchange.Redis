using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Migrate : TestBase
    {
        public Migrate(ITestOutputHelper output) : base (output) { }

        [Fact]
        public async Task Basic()
        {
            var fromConfig = new ConfigurationOptions { EndPoints = { { TestConfig.Current.SecureServer, TestConfig.Current.SecurePort } }, Password = TestConfig.Current.SecurePassword };
            var toConfig = new ConfigurationOptions { EndPoints = { { TestConfig.Current.MasterServer, TestConfig.Current.MasterPort } } };
            using (var from = ConnectionMultiplexer.Connect(fromConfig))
            using (var to = ConnectionMultiplexer.Connect(toConfig))
            {
                RedisKey key = Me();
                var fromDb = from.GetDatabase();
                var toDb = to.GetDatabase();
                fromDb.KeyDelete(key, CommandFlags.FireAndForget);
                toDb.KeyDelete(key, CommandFlags.FireAndForget);
                fromDb.StringSet(key, "foo", flags: CommandFlags.FireAndForget);
                var dest = to.GetEndPoints(true).Single();
                fromDb.KeyMigrate(key, dest);
                await Task.Delay(1000); // this is *meant* to be synchronous at the redis level, but
                // we keep seeing it fail on the CI server where the key has *left* the origin, but
                // has *not* yet arrived at the destination; adding a pause while we investigate with
                // the redis folks
                Assert.False(fromDb.KeyExists(key));
                Assert.True(toDb.KeyExists(key));
                string s = toDb.StringGet(key);
                Assert.Equal("foo", s);
            }
        }
    }
}
