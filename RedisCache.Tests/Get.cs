using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using RedisCache;
using Xunit;

namespace RedisCache.Tests
{
    public class Get
    {
        [Theory]
        [InlineData("testkey")]
        public void Get_CacheMissReturnsNull(string k)
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            mockRedis.Setup(c => c.StringGet(k)).Returns(default(string));
            var cache = new RedisCache(mockRedis.Object);

            var key = new RedisCacheKey(k);

            var result = cache.Get(key);
            Assert.Null(result);
        }

        [Theory]
        [InlineData("testprimary", "testsecondary", "testvalue")]
        public void Get_WithSecondaryKey(string kp, string ks, string v)
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            mockRedis.Setup(c => c.StringGet(kp)).Returns(v);
            mockRedis.Setup(c => c.StringGet(ks)).Returns(kp);
            var cache = new RedisCache(mockRedis.Object);
            
            var value = v;

            var key2 = new RedisCacheKey(new List<string> {ks});

            var result = cache.Get(key2);
            Assert.NotNull(result);
            Assert.Equal(value, result);

            mockRedis.Verify(c => c.StringGet(kp), Times.Once());
        }
    }
}
