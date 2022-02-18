using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class EnvoyTests : TestBase
    {
        public EnvoyTests(ITestOutputHelper output) : base(output) { }

        protected override string GetConfiguration() => TestConfig.Current.ProxyServerAndPort;

        /// <summary>
        /// Tests basic envoy connection with the ability to set and get a key
        /// </summary>
        [Fact]
        public void TestBasicEnvoyConnection()
        {
            using (var muxer = Create(configuration: GetConfiguration(), keepAlive: 1, connectTimeout: 2000, allowAdmin: true, shared: false, proxy: Proxy.Envoyproxy, log: Writer))
            {
                var db = muxer.GetDatabase();

                var key = "foobar";
                var value = "barfoo";
                db.StringSet(key, value);

                var expectedVal = db.StringGet(key);

                Assert.Equal(expectedVal, value);
            }
        }

    }
}
