using System;
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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MassiveBulkOpsFireAndForgetSecure(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
#if DEBUG
                long oldAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
#endif
                RedisKey key = "MBOF";
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
                Output.WriteLine("{2}: Time for {0} ops: {1}ms ({3}); ops/s: {4}", AsyncOpsQty, watch.ElapsedMilliseconds, Me(),
                    preserveOrder ? "preserve order" : "any order",
                    AsyncOpsQty / watch.Elapsed.TotalSeconds);
#if DEBUG
                long newAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
                Output.WriteLine("ResultBox allocations: {0}", newAlloc - oldAlloc);
                Assert.True(newAlloc - oldAlloc <= 2, $"NewAllocs: {newAlloc}, OldAllocs: {oldAlloc}");
#endif
            }
        }

        [Fact]
        public void CheckConfig()
        {
            var config = ConfigurationOptions.Parse(GetConfiguration());
            foreach (var ep in config.EndPoints)
            {
                Output.WriteLine(ep.ToString());
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
            Output.WriteLine("Exception: " + ex.Message);
            Assert.Equal("It was not possible to connect to the redis server(s); to create a disconnected multiplexer, disable AbortOnConnectFail. AuthenticationFailure on PING", ex.Message);
        }
    }
}
