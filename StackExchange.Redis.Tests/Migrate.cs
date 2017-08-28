using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Migrate : TestBase
    {
        public Migrate(ITestOutputHelper output) : base (output) { }

        public void Basic()
        {
            var fromConfig = new ConfigurationOptions { EndPoints = { { TestConfig.Current.MasterServer, TestConfig.Current.SecurePort } }, Password = TestConfig.Current.SecurePassword };
            var toConfig = new ConfigurationOptions { EndPoints = { { TestConfig.Current.MasterServer, TestConfig.Current.MasterPort } } };
            using (var from = ConnectionMultiplexer.Connect(fromConfig))
            using (var to = ConnectionMultiplexer.Connect(toConfig))
            {
                RedisKey key = Me();
                var fromDb = from.GetDatabase();
                var toDb = to.GetDatabase();
                fromDb.KeyDelete(key);
                toDb.KeyDelete(key);
                fromDb.StringSet(key, "foo");
                var dest = to.GetEndPoints(true).Single();
                fromDb.KeyMigrate(key, dest);
                Assert.False(fromDb.KeyExists(key));
                Assert.True(toDb.KeyExists(key));
                string s = toDb.StringGet(key);
                Assert.Equal("foo", s);
            }
        }
    }
}
