using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Expiry : TestBase
    {
        public Expiry(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

        private static string[] GetMap(bool disablePTimes) => disablePTimes ? (new[] { "pexpire", "pexpireat", "pttl" }) : null;

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestBasicExpiryTimeSpan(bool disablePTimes)
        {
            using (var muxer = Create(disabledCommands: GetMap(disablePTimes)))
            {
                RedisKey key = Me();
                var conn = muxer.GetDatabase();
                conn.KeyDelete(key, CommandFlags.FireAndForget);

                conn.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
                var a = conn.KeyTimeToLiveAsync(key);
                conn.KeyExpire(key, TimeSpan.FromHours(1), CommandFlags.FireAndForget);
                var b = conn.KeyTimeToLiveAsync(key);
                conn.KeyExpire(key, (TimeSpan?)null, CommandFlags.FireAndForget);
                var c = conn.KeyTimeToLiveAsync(key);
                conn.KeyExpire(key, TimeSpan.FromHours(1.5), CommandFlags.FireAndForget);
                var d = conn.KeyTimeToLiveAsync(key);
                conn.KeyExpire(key, TimeSpan.MaxValue, CommandFlags.FireAndForget);
                var e = conn.KeyTimeToLiveAsync(key);
                conn.KeyDelete(key, CommandFlags.FireAndForget);
                var f = conn.KeyTimeToLiveAsync(key);

                Assert.Null(await a);
                var time = await b;
                Assert.NotNull(time);
                Assert.True(time > TimeSpan.FromMinutes(59.9) && time <= TimeSpan.FromMinutes(60));
                Assert.Null(await c);
                time = await d;
                Assert.NotNull(time);
                Assert.True(time > TimeSpan.FromMinutes(89.9) && time <= TimeSpan.FromMinutes(90));
                Assert.Null(await e);
                Assert.Null(await f);
            }
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public async Task TestBasicExpiryDateTime(bool disablePTimes, bool utc)
        {
            using (var muxer = Create(disabledCommands: GetMap(disablePTimes)))
            {
                RedisKey key = Me();
                var conn = muxer.GetDatabase();
                conn.KeyDelete(key, CommandFlags.FireAndForget);

                var now = utc ? DateTime.UtcNow : DateTime.Now;
                Log("Now: {0}", now);
                conn.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
                var a = conn.KeyTimeToLiveAsync(key);
                conn.KeyExpire(key, now.AddHours(1), CommandFlags.FireAndForget);
                var b = conn.KeyTimeToLiveAsync(key);
                conn.KeyExpire(key, (DateTime?)null, CommandFlags.FireAndForget);
                var c = conn.KeyTimeToLiveAsync(key);
                conn.KeyExpire(key, now.AddHours(1.5), CommandFlags.FireAndForget);
                var d = conn.KeyTimeToLiveAsync(key);
                conn.KeyExpire(key, DateTime.MaxValue, CommandFlags.FireAndForget);
                var e = conn.KeyTimeToLiveAsync(key);
                conn.KeyDelete(key, CommandFlags.FireAndForget);
                var f = conn.KeyTimeToLiveAsync(key);

                Assert.Null(await a);
                var time = await b;
                Assert.NotNull(time);
                Log("Time: {0}, Expected: {1}-{2}", time, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(60));
                Assert.True(time >= TimeSpan.FromMinutes(59));
                Assert.True(time <= TimeSpan.FromMinutes(60.1));
                Assert.Null(await c);
                time = await d;
                Assert.NotNull(time);
                Assert.True(time >= TimeSpan.FromMinutes(89));
                Assert.True(time <= TimeSpan.FromMinutes(90.1));
                Assert.Null(await e);
                Assert.Null(await f);
            }
        }
    }
}
