using System.Collections.Generic;
using Moq;
using RedisCache;
using Xunit;
using StackExchange.Redis;

namespace RedisCache.Tests
{
    public class Add
    {
        
        [Theory]
        [InlineData("testkey", "testvalue")]
        public void SingletonIsAdded(string k, string v)
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            mockRedis.Setup(c => c.StringGet(k)).Returns(v);

            var cache = new RedisCache(mockRedis.Object);

            var key = new RedisCacheKey(k);
            var value = v;

            cache.Add(key, value);

            mockRedis.Verify(c => c.StringSet(k, v), Times.Once());
        }

        [Theory]
        [InlineData("testkey1", "testvalue1", "testkey2", "testvalue2")]
        public void CanAddMultiples(string k1, string v1, string k2, string v2)
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            mockRedis.Setup(c => c.StringGet(k1)).Returns(v1);
            mockRedis.Setup(c => c.StringGet(k2)).Returns(v2);

            var cache = new RedisCache(mockRedis.Object);
            var key = new RedisCacheKey(k1);

            var key2 = new RedisCacheKey(k2);

            var value = v1;

            var value2 = v2;

            cache.Add(key, value);
            cache.Add(key2, value2);
            
            mockRedis.Verify(c => c.StringSet(k1, v1), Times.Once());
            mockRedis.Verify(c => c.StringSet(k2, v2), Times.Once());
        }

        [Fact]
        public void RequiresPrimaryKey()
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            var cache = new RedisCache(mockRedis.Object);

            var key = new RedisCacheKey(new List<string> {"1234-3"});

            var value = "foo";
            Assert.Throws(typeof(RedisCacheException), () => cache.Add(key, value));
        }
    }
}
