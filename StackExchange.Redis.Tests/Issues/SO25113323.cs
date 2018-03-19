using System;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class SO25113323 : TestBase
    {
        public SO25113323(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void SetExpirationToPassed()
        {
            var key = Me();
            using (var conn =  Create())
            {
                // Given
                var cache = conn.GetDatabase(59);
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
                Assert.True(firstResult); // could set the first time, but this nukes the key
                Assert.False(secondResult); // can't set, since nuked
                Assert.False(exists); // does not exist since nuked
                Assert.Null(ttl); // no expiry since nuked
            }
        }
    }
}
