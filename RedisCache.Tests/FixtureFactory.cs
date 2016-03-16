using Moq;

namespace Saxo.RedisCache.Tests
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
