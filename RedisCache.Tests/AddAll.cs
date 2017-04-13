using System;
using System.Collections.Generic;
using System.Text;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Saxo.RedisCache.Tests
{
    public class AddAll
    {

        [Theory]
        [InlineData("testkey1", "testvalue1", "testkey2", "testvalue2")]
        public void Add_Get_ReturnsSame(string k1, string v1, string k2, string v2)
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            var cache = new RedisCache(mockRedis.Object);

            var key1 = new RedisCacheKey(k1);
            var key2 = new RedisCacheKey(k2);
            
            var value1 = Encoding.ASCII.GetBytes(v1);
            var value2 = Encoding.ASCII.GetBytes(v2);

            cache.AddAll(new List<RedisCacheKey> {key1, key2}, new List<byte[]> {value1, value2});
            
            mockRedis.Verify(c => c.StringSet(new[]
            {
                new KeyValuePair<RedisKey, RedisValue>(k1, value1),
                new KeyValuePair<RedisKey, RedisValue>(k2, value2)
            }, TimeSpan.MinValue), Times.Once());
            
        }

        [Fact]
        public void AddAll_RequiresPrimaryKey()
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            var cache = new RedisCache(mockRedis.Object);

            var key1 = new RedisCacheKey(new List<string> { "addall-2-1" });
            var key2 = new RedisCacheKey(new List<string> { "addall-2-2" });
            
            var value1 = Encoding.ASCII.GetBytes("foo");
            var value2 = Encoding.ASCII.GetBytes("bar");

            Assert.Throws(typeof(RedisCacheException), () => cache.AddAll(new List<RedisCacheKey> { key1, key2 }, new List<byte[]> { value1, value2 }));
        }

        [Theory]
        [InlineData("testpkey1", "testskey1", "testvalue1", "testkey2", "testskey2", "testvalue2")]
        public void AddAll_WithMixed(string pk1, string sk1, string v1, string pk2, string sk2, string v2)
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            var cache = new RedisCache(mockRedis.Object);

            var key1 = new RedisCacheKey(pk1, sk1);
            var key2 = new RedisCacheKey(pk2, sk2);

            var value1 = Encoding.ASCII.GetBytes(v1);
            var value2 = Encoding.ASCII.GetBytes(v2);

            cache.AddAll(new List<RedisCacheKey> { key1, key2 }, new List<byte[]> { value1, value2 });
            
            mockRedis.Verify(c => c.StringSet(new[]
            {
                new KeyValuePair<RedisKey, RedisValue>(pk1, v1),
                new KeyValuePair<RedisKey, RedisValue>(pk2, v2),
                new KeyValuePair<RedisKey, RedisValue>(sk1, pk1),
                new KeyValuePair<RedisKey, RedisValue>(sk2, pk2)
            }, TimeSpan.MinValue), Times.Once());
        }
    }
}
