using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Values : TestBase
    {
        public Values(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void NullValueChecks()
        {
            RedisValue four = 4;
            Assert.False(four.IsNull);
            Assert.True(four.IsInteger);
            Assert.True(four.HasValue);
            Assert.False(four.IsNullOrEmpty);

            RedisValue n = default(RedisValue);
            Assert.True(n.IsNull);
            Assert.False(n.IsInteger);
            Assert.False(n.HasValue);
            Assert.True(n.IsNullOrEmpty);

            RedisValue emptyArr = new byte[0];
            Assert.False(emptyArr.IsNull);
            Assert.False(emptyArr.IsInteger);
            Assert.False(emptyArr.HasValue);
            Assert.True(emptyArr.IsNullOrEmpty);
        }

        [Fact]
        public void FromStream()
        {
            var arr = Encoding.UTF8.GetBytes("hello world");
            var ms = new MemoryStream(arr);
            var val = RedisValue.CreateFrom(ms);
            Assert.Equal("hello world", val);

            ms = new MemoryStream(arr, 1, 6, false, false);
            val = RedisValue.CreateFrom(ms);
            Assert.Equal("ello w", val);

            ms = new MemoryStream(arr, 2, 6, false, true);
            val = RedisValue.CreateFrom(ms);
            Assert.Equal("llo wo", val);
        }
    }
}
