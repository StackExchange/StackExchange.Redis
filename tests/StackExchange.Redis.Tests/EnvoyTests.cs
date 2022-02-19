﻿using System;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class EnvoyTests : TestBase
    {
        public EnvoyTests(ITestOutputHelper output) : base(output) { }

        protected override string GetConfiguration() => TestConfig.Current.ProxyServerAndPort;

        /// <summary>
        /// Tests basic envoy connection with the ability to set and get a key.
        /// </summary>
        [Fact]
        public void TestBasicEnvoyConnection()
        {
            var sb = new StringBuilder();
            Writer.EchoTo(sb);
            try
            {
                using (var muxer = Create(configuration: GetConfiguration(), keepAlive: 1, connectTimeout: 2000, allowAdmin: true, shared: false, proxy: Proxy.Envoyproxy, log: Writer))
                {
                    var db = muxer.GetDatabase();

                    const string key = "foobar";
                    const string value = "barfoo";
                    db.StringSet(key, value);

                    var expectedVal = db.StringGet(key);

                    Assert.Equal(expectedVal, value);
                }
            }
            catch (TimeoutException) when (sb.ToString().Contains("Returned, but incorrectly"))
            {
                Skip.Inconclusive("Envoy server not found.");
            }
        }
    }
}
