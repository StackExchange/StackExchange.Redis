
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RedisCache;
using Xunit;

namespace RedisCache.Tests
{
    public class RemoveAll
    {
        [Fact]
        public void Remove_ReturnsNullAfter()
        {
            var cache = new RedisCache(new RedisCacheTestSettings());

            var key1 = new RedisCacheKey("remall-1-1");
            var key2 = new RedisCacheKey("remall-1-2");

            var value1 = "foo";
            var value2 = "bar";

            cache.AddAll(new List<RedisCacheKey> { key1, key2 }, new List<string> { value1, value2 });

            var result = cache.GetAll(new List<RedisCacheKey> { key1, key2 });
            Assert.Equal(2, result.Count);

            cache.RemoveAll(new List<RedisCacheKey> { key1, key2 });

            result = cache.GetAll(new List<RedisCacheKey> { key1, key2 });
            Assert.Equal(2, result.Count);
        }
        
        [Fact]
        public void Remove_WithSecondaryKey()
        {
            var cache = new RedisCache(new RedisCacheTestSettings());

            var key = new RedisCacheKey("1234-5", "5678-5");

            var value = "foo";

            cache.Add(key, value);

            var key2 = new RedisCacheKey(new List<string> { "5678-5" });

            var result = cache.Get(key);
            Assert.Equal(value, result);

            cache.Remove(key2);

            result = cache.Get(key);
            Assert.Null(result);
        }
    }
}
