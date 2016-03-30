using System.Collections.Generic;
using System.Linq;
using System.Text;
using StackExchange.Redis;
using Xunit;

namespace Saxo.RedisCache.Tests
{
    public class GetAll
    {
        [Theory]
        [MemberData("Data_Simple")]
        [MemberData("Data_Simple_CacheMiss")]
        [MemberData("Data_OnlySecondary")]
        [MemberData("Data_MixedKeys")]
        [MemberData("Data_Mixed_CacheMiss")]
        public void GetAll_VariousData(RedisCacheKey[] keys, string[] missing, string[] primariesForMissing, string[] primaries, string[] values, string[] expectedResults)
        {
            var mockRedis = FixtureFactory.GetMockRedis();
            var cache = new RedisCache(mockRedis.Object);
            RedisKey[] ks = primaries.Select(k => (RedisKey)k).ToArray();
            RedisValue[] vs = values.Select(v => (v!=null)? (RedisValue)Encoding.ASCII.GetBytes(v) : default(RedisValue)).ToArray();
            mockRedis.Setup(c => c.StringGet(ks)).Returns(vs);

            if (missing.Any())
            {
                RedisKey[] ksk = missing.Select(k => (RedisKey)k).ToArray();
                RedisValue[] vsk = primariesForMissing.Select(v => (RedisValue)v).ToArray();
                mockRedis.Setup(c => c.StringGet(ksk)).Returns(vsk);
            }

            var result = cache.GetAll(keys.ToList());
            var expected = expectedResults.Select(v => (v!=null)? Encoding.ASCII.GetBytes(v) : null);
            Assert.NotNull(result);
            Assert.Equal(expectedResults.Count(), result.Count);
            Assert.Equal(expected.ToList(), result);
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
                    new[] {"testvalue1", "testvalue2"},
                    new[] {"testvalue1", "testvalue2"}
                },
                new object[] {
                    new[] {new RedisCacheKey("testkey1"), new RedisCacheKey("testkey2"), new RedisCacheKey("testkey3"), },
                    new string[] {},
                    new string[] {},
                    new[] {"testkey1", "testkey2", "testkey3"},
                    new[] { "testvalue1", "testvalue2", "testvalue3" },
                    new[] { "testvalue1", "testvalue2", "testvalue3" }
                }
            };
            }
        }


        public static IEnumerable<object[]> Data_Simple_CacheMiss
        {
            get
            {
                return new[]
                {
                new object[] {
                    new[] {new RedisCacheKey("testkey1"), new RedisCacheKey("testkey2"), new RedisCacheKey("testkey3"), },
                    new string[] {},
                    new string[] {},
                    new[] {"testkey1", "testkey2", "testkey3"},
                    new[] { "testvalue1", null, "testvalue3" },
                    new[] { "testvalue1", null, "testvalue3" }
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
                    new[] { "testvalue1", "testvalue2"},
                    new[] { "testvalue1", "testvalue2"}
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
                    new[] { "testvalue1", "testvalue2"},
                    new[] { "testvalue1", "testvalue2"}
                },
                new object[] {
                    new[] { new RedisCacheKey(new List<string> { "secondary1" }), new RedisCacheKey("primary2"), new RedisCacheKey(new List<string> { "secondary3" })},
                    new[] {"secondary1", "secondary3"},
                    new[] {"primary1", "primary3"},
                    new[] {"primary1", "primary2", "primary3"},
                    new[] { "testvalue1", "testvalue2", "testvalue3" },
                    new[] { "testvalue1", "testvalue2", "testvalue3" }
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
                    new[] { "testvalue1", "testvalue2"},
                    new[] { "testvalue1", "testvalue2", null}
                },
            };
            }
        }
    }
}
