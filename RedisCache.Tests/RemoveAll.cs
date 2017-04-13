using System.Collections.Generic;
using System.Linq;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Saxo.RedisCache.Tests
{
    public class RemoveAll
    {
        [Theory]
        [MemberData("Data_Simple")]
        [MemberData("Data_OnlySecondary")]
        [MemberData("Data_MixedKeys")]
        [MemberData("Data_Mixed_CacheMiss")]
        public void RemoveAll_VariousData(RedisCacheKey[] keys, string[] missing, string[] primariesForMissing, string[] primaries)
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            var cache = new Saxo.RedisCache.RedisCache(mockRedis.Object);

            if (missing.Any())
            {
                RedisKey[] ksk = missing.Select(k => (RedisKey)k).ToArray();
                RedisValue[] vsk = primariesForMissing.Select(v => (RedisValue)v).ToArray();
                mockRedis.Setup(c => c.StringGet(ksk)).Returns(vsk);
            }

            cache.RemoveAll(keys.ToList());

            if (missing.Any())
            {
                RedisKey[] ksk = missing.Select(k => (RedisKey)k).ToArray();
                mockRedis.Verify(c => c.StringGet(ksk), Times.Once());
            }

            RedisKey[] ks = primaries.Select(k => (RedisKey)k).ToArray();
            mockRedis.Verify(c => c.KeyDelete(ks), Times.Once());
        }

        public static IEnumerable<object[]> Data_Simple
        {
            get
            {
                return new[]
                {
                new object[] {
                    new[] {new RedisCacheKey("testkey1"), new RedisCacheKey("testkey2"), },
                    new string[] {},
                    new string[] {},
                    new [] {"testkey1", "testkey2"},
                },
                new object[] {
                    new[] {new RedisCacheKey("testkey1"), new RedisCacheKey("testkey2"), new RedisCacheKey("testkey3"), },
                    new string[] {},
                    new string[] {},
                    new[] {"testkey1", "testkey2", "testkey3"},
                }
            };
            }
        }
        
        public static IEnumerable<object[]> Data_OnlySecondary
        {
            get
            {
                return new[]
                {
                new object[] {
                    new[] {new RedisCacheKey(new List<string> {"secondary1"}), new RedisCacheKey(new List<string> { "secondary2" })},
                    new[] { "secondary1", "secondary2"},
                    new[] {"primary1", "primary2"},
                    new[] {"primary1", "primary2"},
                }
            };
            }
        }

        public static IEnumerable<object[]> Data_MixedKeys
        {
            get
            {
                return new[]
                {
                new object[] {
                    new[] {new RedisCacheKey("primary1"), new RedisCacheKey(new List<string> { "secondary2" })},
                    new[] {"secondary2"},
                    new[] {"primary2"},
                    new[] {"primary1", "primary2"},
                },
                new object[] {
                    new[] { new RedisCacheKey(new List<string> { "secondary1" }), new RedisCacheKey("primary2"), new RedisCacheKey(new List<string> { "secondary3" })},
                    new[] {"secondary1", "secondary3"},
                    new[] {"primary1", "primary3"},
                    new[] {"primary1", "primary2", "primary3"},
                },
            };
            }
        }

        public static IEnumerable<object[]> Data_Mixed_CacheMiss
        {
            get
            {
                return new[]
                {
                new object[] {
                    new[] { new RedisCacheKey(new List<string> { "secondary1" }), new RedisCacheKey("primary2"), new RedisCacheKey(new List<string> { "secondary3" })},
                    new[] {"secondary1", "secondary3"},
                    new[] {"primary1", null},
                    new[] {"primary1", "primary2"},
                },
            };
            }
        }
    }
}
