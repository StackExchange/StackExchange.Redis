using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class EnvoyTests : TestBase
    {
        public EnvoyTests(ITestOutputHelper output) : base(output) { }

        /// <summary>
        /// Tests basic envoy connection with the ability to set and get a key
        /// NOTE: For running this test locally, please use WSL and run `./start-all.sh` to start all server
        /// TODO: Local testing for windows
        /// </summary>
        [Fact]
        public void TestBasicEnvoyConnection()
        {
            using (var muxer = Create(configuration: GetProxyConfiguration(), keepAlive: 1, connectTimeout: 10000, allowAdmin: true, shared: false, proxy: Proxy.Envoyproxy))
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
