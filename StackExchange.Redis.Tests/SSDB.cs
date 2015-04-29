using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class SSDB : TestBase
    {
        [Test]
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
                Assert.IsTrue(db.StringGet(key).IsNull);
                db.StringSet(key, "abc");
                Assert.AreEqual("abc", (string)db.StringGet(key));
            }
        }
    }
}
