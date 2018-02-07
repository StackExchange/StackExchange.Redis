using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Bits : TestBase
    {
        public Bits(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void BasicOps()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();

                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringSetBit(key, 10, true);
                Assert.True(db.StringGetBit(key, 10));
                Assert.False(db.StringGetBit(key, 11));
            }
        }
    }
}