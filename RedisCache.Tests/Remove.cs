using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RedisCache;
using Xunit;

namespace RedisCache.Tests
{
    public class Remove
    {
        [Fact]
        public void Remove_ReturnsNullAfter()
        {
            var cache = new RedisCache(new RedisCacheTestSettings());

            var key = new RedisCacheKey("1234-4");

            var value = "foo";

            cache.Add(key, value);

            var result = cache.Get(key);
            Assert.Equal(value, result);

            cache.Remove(key);

            result = cache.Get(key);
            Assert.Null(result);
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
