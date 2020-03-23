#pragma warning disable RCS1090 // Call 'ConfigureAwait(false)'.

using System;
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
            var fromConfig = new ConfigurationOptions { EndPoints = { { TestConfig.Current.SecureServer, TestConfig.Current.SecurePort } }, Password = TestConfig.Current.SecurePassword, AllowAdmin = true };
            var toConfig = new ConfigurationOptions { EndPoints = { { TestConfig.Current.MasterServer, TestConfig.Current.MasterPort } }, AllowAdmin = true };
            using (var from = ConnectionMultiplexer.Connect(fromConfig))
            using (var to = ConnectionMultiplexer.Connect(toConfig))
            {
                if (await IsWindows(from) || await IsWindows(to))
                    Skip.Inconclusive("'migrate' is unreliable on redis-64");

                RedisKey key = Me();
                var fromDb = from.GetDatabase();
                var toDb = to.GetDatabase();
                fromDb.KeyDelete(key, CommandFlags.FireAndForget);
                toDb.KeyDelete(key, CommandFlags.FireAndForget);
                fromDb.StringSet(key, "foo", flags: CommandFlags.FireAndForget);
                var dest = to.GetEndPoints(true).Single();
                fromDb.KeyMigrate(key, dest, migrateOptions: MigrateOptions.Replace);

                // this is *meant* to be synchronous at the redis level, but
                // we keep seeing it fail on the CI server where the key has *left* the origin, but
                // has *not* yet arrived at the destination; adding a pause while we investigate with
                // the redis folks
                await UntilCondition(TimeSpan.FromSeconds(5), () => !fromDb.KeyExists(key) && toDb.KeyExists(key));

                Assert.False(fromDb.KeyExists(key));
                Assert.True(toDb.KeyExists(key));
                string s = toDb.StringGet(key);
                Assert.Equal("foo", s);
            }
        }

        private async Task<bool> IsWindows(ConnectionMultiplexer conn)
        {
            var server = conn.GetServer(conn.GetEndPoints().First());
            var section = (await server.InfoAsync("server")).Single();
            var os = section.FirstOrDefault(
                x => string.Equals("os", x.Key, StringComparison.OrdinalIgnoreCase));
            // note: WSL returns things like "os:Linux 4.4.0-17134-Microsoft x86_64"
            return string.Equals("windows", os.Value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
