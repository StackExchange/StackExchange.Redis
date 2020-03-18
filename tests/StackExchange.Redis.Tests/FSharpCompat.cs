using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class FSharpCompat : TestBase
    {
        public FSharpCompat(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void RedisKeyConstructor()
        {
            Assert.Equal(default, new RedisKey());
            Assert.Equal((RedisKey)"MyKey", new RedisKey("MyKey"));
            Assert.Equal((RedisKey)"MyKey2", new RedisKey(null, "MyKey2"));
        }

        [Fact]
        public void RedisValueConstructor()
        {
            Assert.Equal(default, new RedisValue());
            Assert.Equal((RedisValue)"MyKey", new RedisValue("MyKey"));
            Assert.Equal((RedisValue)"MyKey2", new RedisValue("MyKey2", 0));
        }
    }
}
