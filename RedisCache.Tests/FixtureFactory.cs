using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using RedisCache;
using StackExchange.Redis;

namespace RedisCache.Tests
{
    class FixtureFactory
    {
        internal static Mock<IRedisImplementation> GetMockRedis()
        {
            var mockRedis = new Mock<IRedisImplementation>();
            return mockRedis;
        }
    }
}
