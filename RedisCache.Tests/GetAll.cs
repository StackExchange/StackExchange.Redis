using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RedisCache;
using Xunit;

namespace RedisCache.Tests
{
    public class GetAll
    {
        [Fact]
        public void Add_GetAll_ReturnsSame()
        {
            var cache = new RedisCache(new RedisCacheTestSettings());


            var key1 = new RedisCacheKey("getall-1-1");
            var key2 = new RedisCacheKey("getall-1-2");

            var value1 = "foo";
            var value2 = "bar";

            cache.Add(key1, value1);
            cache.Add(key2, value2);

            var result = cache.GetAll(new List<RedisCacheKey> { key1, key2 });

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(value1, result);
            Assert.Contains(value2, result);
        }

        [Fact]
        public void GetAll_WithSecondary()
        {
            var cache = new RedisCache(new RedisCacheTestSettings());

            var key1 = new RedisCacheKey("getall-2-1", "sec-2-1");
            var key2 = new RedisCacheKey("getall-2-2", "sec-2-2");

            var value1 = "foo";
            var value2 = "bar";

            cache.Add(key1, value1);
            cache.Add(key2, value2);

            key1 = new RedisCacheKey(new List<string> {"sec-2-1"});
            key2 = new RedisCacheKey(new List<string> {"sec-2-2"});

            var result = cache.GetAll(new List<RedisCacheKey> { key1, key2 });

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(value1, result);
            Assert.Contains(value2, result);
        }

        [Fact]
        public void GetAll_WithMixed()
        {
            var cache = new RedisCache(new RedisCacheTestSettings());

            var key1 = new RedisCacheKey("getall-3-1", "sec-3-1");
            var key2 = new RedisCacheKey("getall-3-2", "sec-3-2");

            var value1 = "foo";
            var value2 = "bar";

            cache.Add(key1, value1);
            cache.Add(key2, value2);

            key1 = new RedisCacheKey(new List<string> { "sec-3-1" });
            key2 = new RedisCacheKey("getall-3-2");

            var result = cache.GetAll(new List<RedisCacheKey> { key1, key2 });

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(value1, result);
            Assert.Contains(value2, result);
        }

        [Fact]
        public void GetAll_Mixed_ReturnsSorted()
        {
            var cache = new RedisCache(new RedisCacheTestSettings());

            var key1 = new RedisCacheKey("getall-4-1", "sec-4-1");
            var key2 = new RedisCacheKey("getall-4-2", "sec-4-2");
            var key3 = new RedisCacheKey("getall-4-3", "sec-4-3");
            var key4 = new RedisCacheKey("getall-4-4", "sec-4-4");

            var value1 = "val1";
            var value2 = "val2";
            var value3 = "val3";
            var value4 = "val4";

            cache.Add(key1, value1);
            cache.Add(key2, value2);
            cache.Add(key3, value3);
            cache.Add(key4, value4);

            key1 = new RedisCacheKey(new List<string> { "sec-4-1" });
            key2 = new RedisCacheKey("getall-4-2");
            key3 = new RedisCacheKey(new List<string> { "sec-4-3" });
            key4 = new RedisCacheKey("getall-4-4");

            var result = cache.GetAll(new List<RedisCacheKey> { key1, key2, key3, key4 });

            Assert.NotNull(result);
            Assert.Equal(4, result.Count);
            Assert.Equal(new List<string>{value1, value2, value3, value4}, result);
        }

        [Fact]
        public void GetAll_Mixed_CacheMissIsNull()
        {
            var cache = new RedisCache(new RedisCacheTestSettings());

            var key1 = new RedisCacheKey("getall-5-1", "sec-5-1");
            var key4 = new RedisCacheKey("getall-5-4", "sec-5-4");

            var value1 = "val1";
            var value4 = "val4";

            cache.Add(key1, value1);
            cache.Add(key4, value4);

            key1 = new RedisCacheKey(new List<string> { "sec-5-1" });
            var key2 = new RedisCacheKey("getall-5-2");
            var key3 = new RedisCacheKey(new List<string> { "sec-5-3" });
            key4 = new RedisCacheKey("getall-5-4");

            var result = cache.GetAll(new List<RedisCacheKey> { key1, key2, key3, key4 });

            Assert.NotNull(result);
            Assert.Equal(4, result.Count);
            Assert.Equal(new List<string> { value1, default(string), default(string), value4 }, result);
        }
    }
}
