using System;
using System.Collections.Generic;
using System.Text;
using Moq;
using Xunit;

namespace Saxo.RedisCache.Tests
{
    public class Add
    {
        [Theory]
        [InlineData("testkey", "testvalue")]
        public void SingletonIsAdded(string k, string v)
        {
            var value = Encoding.ASCII.GetBytes(v);
            var mockRedis = FixtureFactory.GetMockRedis();
            mockRedis.Setup(c => c.StringGet(k)).Returns(value);

            var cache = new RedisCache(mockRedis.Object);

            var key = new RedisCacheKey(k);

            cache.Add(key, value);

            mockRedis.Verify(c => c.StringSet(k, v, TimeSpan.MinValue), Times.Once());
        }

        [Theory]
        [InlineData("testkey1", "testvalue1", "testkey2", "testvalue2")]
        public void CanAddMultiples(string k1, string v1, string k2, string v2)
        {
            var value1 = Encoding.ASCII.GetBytes(v1);
            var value2 = Encoding.ASCII.GetBytes(v2);

            var mockRedis = FixtureFactory.GetMockRedis();
            mockRedis.Setup(c => c.StringGet(k1)).Returns(value1);
            mockRedis.Setup(c => c.StringGet(k2)).Returns(value2);

            var cache = new RedisCache(mockRedis.Object);
            var key = new RedisCacheKey(k1);

            var key2 = new RedisCacheKey(k2);
            
            cache.Add(key, value1);
            cache.Add(key2, value2);
            
            mockRedis.Verify(c => c.StringSet(k1, v1, TimeSpan.MinValue), Times.Once());
            mockRedis.Verify(c => c.StringSet(k2, v2, TimeSpan.MinValue), Times.Once());
        }

        [Fact]
        public void RequiresPrimaryKey()
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            var cache = new RedisCache(mockRedis.Object);

            var key = new RedisCacheKey(new List<string> {"1234-3"});

            var value = Encoding.ASCII.GetBytes("foo");
            Assert.Throws(typeof(RedisCacheException), () => cache.Add(key, value));
        }
    }
}
