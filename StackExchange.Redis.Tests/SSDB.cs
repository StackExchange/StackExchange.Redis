using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class SSDB : TestBase
    {
        public SSDB(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void ConnectToSSDB()
        {
            var config = new ConfigurationOptions
            {
                EndPoints = { { "ubuntu", 8888 } },
                CommandMap = CommandMap.SSDB
            };
            RedisKey key = Me();
            using (var conn = ConnectionMultiplexer.Connect(config))
            {
                var db = conn.GetDatabase(0);
                db.KeyDelete(key);
                Assert.True(db.StringGet(key).IsNull);
                db.StringSet(key, "abc");
                Assert.Equal("abc", (string)db.StringGet(key));
            }
        }
    }
}
