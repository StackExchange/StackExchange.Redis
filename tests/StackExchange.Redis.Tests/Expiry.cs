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

        private static string[]? GetMap(bool disablePTimes) => disablePTimes ? (new[] { "pexpire", "pexpireat", "pttl" }) : null;

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
        [InlineData(true)]
        [InlineData(false)]
        public void TestExpiryOptions(bool disablePTimes)
        {
            using var muxer = Create(disabledCommands: GetMap(disablePTimes));
            Skip.IfBelow(muxer, RedisFeatures.v7_0_0_rc1);

            var key = Me();
            var conn = muxer.GetDatabase();
            conn.KeyDelete(key, CommandFlags.FireAndForget);
            conn.StringSet(key, "value", flags: CommandFlags.FireAndForget);

            // The key has no expiry
            Assert.False(conn.KeyExpire(key, TimeSpan.FromHours(1), ExpiryWhen.HasExpiry));
            Assert.True(conn.KeyExpire(key, TimeSpan.FromHours(1), ExpiryWhen.HasNoExpiry));

            // The key has an existing expiry
            Assert.True(conn.KeyExpire(key, TimeSpan.FromHours(1), ExpiryWhen.HasExpiry));
            Assert.False(conn.KeyExpire(key, TimeSpan.FromHours(1), ExpiryWhen.HasNoExpiry));

            // Set only when the new expiry is greater than current one
            Assert.True(conn.KeyExpire(key, TimeSpan.FromHours(1.5), ExpiryWhen.GreaterThanCurrentExpiry));
            Assert.False(conn.KeyExpire(key, TimeSpan.FromHours(0.5), ExpiryWhen.GreaterThanCurrentExpiry));

            // Set only when the new expiry is less than current one
            Assert.True(conn.KeyExpire(key, TimeSpan.FromHours(0.5), ExpiryWhen.LessThanCurrentExpiry));
            Assert.False(conn.KeyExpire(key, TimeSpan.FromHours(1.5), ExpiryWhen.LessThanCurrentExpiry));
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
                var serverTime = GetServer(muxer).Time();
                Log("Server time: {0}", serverTime);
                var offset = DateTime.UtcNow - serverTime;

                Log("Now (local time): {0}", now);
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
                var timeResult = await b;
                Assert.NotNull(timeResult);
                TimeSpan time = timeResult.Value;

                // Adjust for server time offset, if any when checking expectations
                time -= offset;

                Log("Time: {0}, Expected: {1}-{2}", time, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(60));
                Assert.True(time >= TimeSpan.FromMinutes(59));
                Assert.True(time <= TimeSpan.FromMinutes(60.1));
                Assert.Null(await c);

                timeResult = await d;
                Assert.NotNull(timeResult);
                time = timeResult.Value;

                Assert.True(time >= TimeSpan.FromMinutes(89));
                Assert.True(time <= TimeSpan.FromMinutes(90.1));
                Assert.Null(await e);
                Assert.Null(await f);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task KeyExpiryTimeAsync(bool disablePTimes)
        {
            using var muxer = Create(disabledCommands: GetMap(disablePTimes));
            Skip.IfBelow(muxer, RedisFeatures.v7_0_0_rc1);
            var conn = muxer.GetDatabase();

            var key = Me();
            conn.KeyDelete(key, CommandFlags.FireAndForget);

            var expireTime = TimeSpan.FromHours(1);
            conn.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
            conn.KeyExpire(key, expireTime, CommandFlags.FireAndForget);

            var time = await conn.KeyExpireTimeAsync(key);
            Assert.NotNull(time);
            Assert.True(time > expireTime);

            // Without associated expiration time
            conn.KeyDelete(key, CommandFlags.FireAndForget);
            conn.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
            time = await conn.KeyExpireTimeAsync(key);
            Assert.Null(time);

            // Non existing key
            conn.KeyDelete(key, CommandFlags.FireAndForget);
            time = await conn.KeyExpireTimeAsync(key);
            Assert.Null(time);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void KeyExpiryTime(bool disablePTimes)
        {
            using var muxer = Create(disabledCommands: GetMap(disablePTimes));
            Skip.IfBelow(muxer, RedisFeatures.v7_0_0_rc1);

            var key = Me();
            var conn = muxer.GetDatabase();
            conn.KeyDelete(key, CommandFlags.FireAndForget);

            var expireTime = TimeSpan.FromHours(1);
            conn.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
            conn.KeyExpire(key, expireTime, CommandFlags.FireAndForget);

            var time = conn.KeyExpireTime(key);
            Assert.NotNull(time);
            Assert.True(time > expireTime);

            // Without associated expiration time
            conn.KeyDelete(key, CommandFlags.FireAndForget);
            conn.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
            time = conn.KeyExpireTime(key);
            Assert.Null(time);

            // Non existing key
            conn.KeyDelete(key, CommandFlags.FireAndForget);
            time = conn.KeyExpireTime(key);
            Assert.Null(time);
        }
    }
}
