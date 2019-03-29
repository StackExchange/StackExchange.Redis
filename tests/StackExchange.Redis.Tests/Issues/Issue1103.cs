using System.Globalization;
using Xunit;
using Xunit.Abstractions;
using static StackExchange.Redis.RedisValue;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue1103 : TestBase
    {
        public Issue1103(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(142205255210238005UL, (int)StorageType.Int64)]
        [InlineData(ulong.MaxValue, (int)StorageType.UInt64)]
        [InlineData(ulong.MinValue, (int)StorageType.Int64)]
        [InlineData(0x8000000000000000UL, (int)StorageType.UInt64)]
        [InlineData(0x8000000000000001UL, (int)StorageType.UInt64)]
        [InlineData(0x7FFFFFFFFFFFFFFFUL, (int)StorageType.Int64)]
        public void LargeUInt64StoredCorrectly(ulong value, int storageType)
        {
            RedisKey key = Me();
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                RedisValue typed = value;

                // only need UInt64 for 64-bits
                Assert.Equal((StorageType)storageType, typed.Type);
                db.StringSet(key, typed);
                var fromRedis = db.StringGet(key);

                Log($"{fromRedis.Type}: {fromRedis}");
                Assert.Equal(StorageType.Raw, fromRedis.Type);
                Assert.Equal(value, (ulong)fromRedis);
                Assert.Equal(value.ToString(CultureInfo.InvariantCulture), fromRedis.ToString());

                var simplified = fromRedis.Simplify();
                Log($"{simplified.Type}: {simplified}");
                Assert.Equal((StorageType)storageType, typed.Type);
                Assert.Equal(value, (ulong)simplified);
                Assert.Equal(value.ToString(CultureInfo.InvariantCulture), fromRedis.ToString());
            }
        }
    }
}
