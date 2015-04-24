using System;
using System.Threading;
using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
    [TestFixture]
    public class SO24807536 : TestBase
    {
        public void Exec()
        {
            var key = Me();
            using(var conn = Create())
            {
                var cache = conn.GetDatabase();

                // setup some data
                cache.KeyDelete(key);
                cache.HashSet(key, "full", "some value");
                cache.KeyExpire(key, TimeSpan.FromSeconds(3));

                // test while exists
                var exists = cache.KeyExists(key);
                var ttl = cache.KeyTimeToLive(key);
                var fullWait = cache.HashGetAsync(key, "full", flags: CommandFlags.None);
                Assert.IsTrue(exists, "key exists");
                Assert.IsNotNull(ttl, "ttl");
                Assert.AreEqual("some value", (string)fullWait.Result);

                // wait for expiry
                Thread.Sleep(TimeSpan.FromSeconds(4));

                // test once expired
                exists = cache.KeyExists(key);
                ttl = cache.KeyTimeToLive(key);
                fullWait = cache.HashGetAsync(key, "full", flags: CommandFlags.None);                
                Assert.IsFalse(exists, "key exists");
                Assert.IsNull(ttl, "ttl");
                Assert.IsNull((string)fullWait.Result);
            }
        }
    }
}
