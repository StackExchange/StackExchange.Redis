using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class SO24807536 : TestBase
    {
        public SO24807536(ITestOutputHelper output) : base (output) { }

        [Fact]
        public async Task Exec()
        {
            var key = Me();
            using(var conn = Create())
            {
                var cache = conn.GetDatabase();

                // setup some data
                cache.KeyDelete(key, CommandFlags.FireAndForget);
                cache.HashSet(key, "full", "some value", flags: CommandFlags.FireAndForget);
                cache.KeyExpire(key, TimeSpan.FromSeconds(1), CommandFlags.FireAndForget);

                // test while exists
                var keyExists = cache.KeyExists(key);
                var ttl = cache.KeyTimeToLive(key);
                var fullWait = cache.HashGetAsync(key, "full", flags: CommandFlags.None);
                Assert.True(keyExists, "key exists");
                Assert.NotNull(ttl);
                Assert.Equal("some value", fullWait.Result);

                // wait for expiry
                await Task.Delay(2000).ForAwait();

                // test once expired
                keyExists = cache.KeyExists(key);
                ttl = cache.KeyTimeToLive(key);
                fullWait = cache.HashGetAsync(key, "full", flags: CommandFlags.None);

                Assert.False(keyExists);
                Assert.Null(ttl);
                var r = await fullWait;
                Assert.True(r.IsNull);
                Assert.Null((string)r);
            }
        }
    }
}
