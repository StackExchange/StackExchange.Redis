using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Bits : TestBase
    {
        [Test]
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