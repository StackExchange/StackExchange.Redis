using NUnit.Framework;
using System.IO;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class HyperLogLog : TestBase
    {
        [Test]
        public void SingleKeyLength()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = "hll1";

                db.HyperLogLogAdd(key, "a");
                db.HyperLogLogAdd(key, "b");
                db.HyperLogLogAdd(key, "c");

                Assert.IsTrue(db.HyperLogLogLength(key) > 0);
            }
        }

        [Test]
        public void MultiKeyLength()
        {
            using (var conn = Create(useSharedSocketManager: true))
            {
                var db = conn.GetDatabase();
                RedisKey[] keys = { "hll1", "hll2", "hll3" };

                db.HyperLogLogAdd(keys[0], "a");
                db.HyperLogLogAdd(keys[1], "b");
                db.HyperLogLogAdd(keys[2], "c");

                Assert.IsTrue(db.HyperLogLogLength(keys) > 0);
            }
        }
    }
}
