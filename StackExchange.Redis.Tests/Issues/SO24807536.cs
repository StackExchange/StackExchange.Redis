using System;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class SO24807536 : TestBase
    {
        public SO24807536(ITestOutputHelper output) : base (output) { }

        [Fact]
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
                var keyExists = cache.KeyExists(key);
                var ttl = cache.KeyTimeToLive(key);
                var fullWait = cache.HashGetAsync(key, "full", flags: CommandFlags.None);
                Assert.True(keyExists, "key exists");
                Assert.NotNull(ttl);
                Assert.Equal("some value", fullWait.Result);

                // wait for expiry
                Thread.Sleep(TimeSpan.FromSeconds(4));

                // test once expired
                keyExists = cache.KeyExists(key);
                ttl = cache.KeyTimeToLive(key);
                fullWait = cache.HashGetAsync(key, "full", flags: CommandFlags.None);
                Assert.False(keyExists);
                Assert.Null(ttl);
                Assert.Null((string)fullWait.Result);
            }
        }
    }
}
