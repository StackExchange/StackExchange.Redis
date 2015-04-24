using System;
using System.Threading;
using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
    [TestFixture]
    public class SO25113323 : TestBase
    {
        [Test]
        public void SetExpirationToPassed()
        {
            var key = Me();
            using (var conn =  Create())
            {
                // Given
                var cache = conn.GetDatabase();
                cache.KeyDelete(key);
                cache.HashSet(key, "full", "test", When.NotExists, CommandFlags.PreferMaster);

                Thread.Sleep(10 * 1000);

                // When
                var expiresOn = DateTime.UtcNow.AddSeconds(-10);

                var firstResult = cache.KeyExpire(key, expiresOn, CommandFlags.PreferMaster);
                var secondResult = cache.KeyExpire(key, expiresOn, CommandFlags.PreferMaster);
                var exists = cache.KeyExists(key);
                var ttl = cache.KeyTimeToLive(key);

                // Then
                Assert.IsTrue(firstResult, "first"); // could set the first time, but this nukes the key
                Assert.IsFalse(secondResult, "second"); // can't set, since nuked
                Assert.IsFalse(exists, "exists"); // does not exist since nuked
                Assert.IsNull(ttl, "ttl"); // no expiry since nuked
            }
        }
    }
}
