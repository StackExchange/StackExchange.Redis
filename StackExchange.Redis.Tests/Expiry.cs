using System;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Expiry : TestBase
    {
        static string[] GetMap(bool disablePTimes)
        {
            if(disablePTimes)
            {
                return new[] { "pexpire", "pexpireat", "pttl" };
            }
            return null;
        }
        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void TestBasicExpiryTimeSpan(bool disablePTimes)
        {
            using(var muxer = Create(disabledCommands: GetMap(disablePTimes)))
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

                Assert.IsNull(muxer.Wait(a));
                var time = muxer.Wait(b);
                Assert.IsNotNull(time);
                Assert.IsTrue(time > TimeSpan.FromMinutes(59.9) && time <= TimeSpan.FromMinutes(60));
                Assert.IsNull(muxer.Wait(c));
                time = muxer.Wait(d);
                Assert.IsNotNull(time);
                Assert.IsTrue(time > TimeSpan.FromMinutes(89.9) && time <= TimeSpan.FromMinutes(90));
                Assert.IsNull(muxer.Wait(e));
            }
        }

        [Test]
        [TestCase(true, true)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public void TestBasicExpiryDateTime(bool disablePTimes, bool utc)
        {
            using (var muxer = Create(disabledCommands: GetMap(disablePTimes)))
            {
                RedisKey key = Me();
                var conn = muxer.GetDatabase();
                conn.KeyDelete(key, CommandFlags.FireAndForget);

                var now = utc ? DateTime.UtcNow : new DateTime(DateTime.UtcNow.Ticks + TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time").BaseUtcOffset.Ticks, DateTimeKind.Local);
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

                Assert.IsNull(muxer.Wait(a));
                var time = muxer.Wait(b);
                Assert.IsNotNull(time);
                Console.WriteLine(time);
                Assert.IsTrue(time > TimeSpan.FromMinutes(59.9) && time <= TimeSpan.FromMinutes(60));
                Assert.IsNull(muxer.Wait(c));
                time = muxer.Wait(d);
                Assert.IsNotNull(time);
                Assert.IsTrue(time > TimeSpan.FromMinutes(89.9) && time <= TimeSpan.FromMinutes(90));
                Assert.IsNull(muxer.Wait(e));
            }
        }
    }
}
