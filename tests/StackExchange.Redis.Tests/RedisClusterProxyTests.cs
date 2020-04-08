using System;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class RedisClusterProxyTests : TestBase
    {
        public RedisClusterProxyTests(ITestOutputHelper output) : base(output) { }

        protected override string GetConfiguration() => "127.0.0.1,proxy=RedisClusterProxy,version=5.0";

        [Fact(Skip = "No CI build for this yet")]
        public void CanConnectToClusterProxy()
        {
            using (var conn = Create())
            {
                var expected = Guid.NewGuid().ToString();
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.StringSet(key, expected);
                var actual = (string)db.StringGet(key);
                Assert.Equal(expected, actual);

                // check it knows that we're dealing with a cluster
                var server = conn.GetServer(conn.GetEndPoints()[0]);
                Assert.Equal(ServerType.RedisClusterProxy, server.ServerType);
                _  = server.Echo("abc");

                var ex = Assert.Throws<NotSupportedException>(() => conn.GetSubscriber("abc"));
                Assert.Equal("The pub/sub API is not available via RedisClusterProxy", ex.Message);

                // test a script
                const string LUA_SCRIPT = "return redis.call('info')";
                var name = (string)db.ScriptEvaluate(LUA_SCRIPT);
                Log($"client: {name}");
                // run it twice to check we didn't rely on script hashing (SCRIPT is disabled)
                _ = db.ScriptEvaluate(LUA_SCRIPT);
            }
        }
    }
}
