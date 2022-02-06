using System;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class EnvoyproxyTests : TestBase
    {
        public EnvoyproxyTests(ITestOutputHelper output) : base(output) { }

        protected override string GetConfiguration() => "127.0.0.1,proxy=EnvoyProxy";

        [Fact(Skip = "No CI build for this yet")]
        public void CanConnectToEnvoyProxy()
        {
            using (var conn = Create())
            {
                var expected = Guid.NewGuid().ToString();
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.StringSet(key, expected);
                var actual = (string)db.StringGet(key);
                Assert.Equal(expected, actual);

                // check it knows that we're dealing with envoy proxy
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                Assert.Equal(ServerType.Envoyproxy, server.ServerType); ;
                _ = server.Echo("abc");

                var ex = Assert.Throws<NotSupportedException>(() => conn.GetSubscriber("abc"));
                Assert.Equal("The pub/sub API is not available via EnvoyProxy", ex.Message);
            }
        }

    }
}
