using System.Collections.Generic;
using Moq;
using Xunit;

namespace Saxo.RedisCache.Tests
{
    public class Remove
    {
        [Theory]
        [InlineData("testprimary")]
        public void Remove_WithPrimaryKey(string kp)
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            var cache = new Saxo.RedisCache.RedisCache(mockRedis.Object);
            
            var key1 = new RedisCacheKey(kp);

            cache.Remove(key1);
            
            mockRedis.Verify(c => c.KeyDelete(kp), Times.Once());
        }

        [Theory]
        [InlineData("testprimary", "testsecondary")]
        public void Remove_WithSecondaryKey(string kp, string ks)
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            mockRedis.Setup(c => c.StringGet(ks)).Returns(kp);
            var cache = new Saxo.RedisCache.RedisCache(mockRedis.Object);
            
            var key2 = new RedisCacheKey(new List<string> { ks });

            cache.Remove(key2);

            mockRedis.Verify(c => c.StringGet(ks), Times.Once());
            mockRedis.Verify(c => c.KeyDelete(kp), Times.Once());
        }
        
    }
}
