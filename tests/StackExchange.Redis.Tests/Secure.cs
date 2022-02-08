using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)]
    public class Secure : TestBase
    {
        protected override string GetConfiguration() =>
            TestConfig.Current.SecureServerAndPort + ",password=" + TestConfig.Current.SecurePassword + ",name=MyClient";

        public Secure(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void MassiveBulkOpsFireAndForgetSecure()
        {
            using (var muxer = Create())
            {
                RedisKey key = Me();
                var conn = muxer.GetDatabase();
                conn.Ping();

                var watch = Stopwatch.StartNew();

                for (int i = 0; i <= AsyncOpsQty; i++)
                {
                    conn.StringSet(key, i, flags: CommandFlags.FireAndForget);
                }
                int val = (int)conn.StringGet(key);
                Assert.Equal(AsyncOpsQty, val);
                watch.Stop();
                Log("{2}: Time for {0} ops: {1}ms (any order); ops/s: {3}", AsyncOpsQty, watch.ElapsedMilliseconds, Me(),
                    AsyncOpsQty / watch.Elapsed.TotalSeconds);
            }
        }

        [Fact]
        public void CheckConfig()
        {
            var config = ConfigurationOptions.Parse(GetConfiguration());
            foreach (var ep in config.EndPoints)
            {
                Log(ep.ToString());
            }
            Assert.Single(config.EndPoints);
            Assert.Equal("changeme", config.Password);
        }

        [Fact]
        public void Connect()
        {
            using (var server = Create())
            {
                server.GetDatabase().Ping();
            }
        }

        [Theory]
        [InlineData("wrong")]
        [InlineData("")]
        public async Task ConnectWithWrongPassword(string password)
        {
            var config = ConfigurationOptions.Parse(GetConfiguration());
            config.Password = password;
            config.ConnectRetry = 0; // we don't want to retry on closed sockets in this case.

            var ex = await Assert.ThrowsAsync<RedisConnectionException>(async () =>
            {
                SetExpectedAmbientFailureCount(-1);
                using (var conn = await ConnectionMultiplexer.ConnectAsync(config, Writer).ConfigureAwait(false))
                {
                    conn.GetDatabase().Ping();
                }
            }).ConfigureAwait(false);
            Log("Exception: " + ex.Message);
            Assert.StartsWith("It was not possible to connect to the redis server(s). There was an authentication failure; check that passwords (or client certificates) are configured correctly.", ex.Message);
        }
    }
}
