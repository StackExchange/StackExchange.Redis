using Xunit.Sdk;

namespace StackExchange.Redis.Tests;

public interface IRedisTest : IXunitTestCase
{
    public RedisProtocol Protocol { get; set; }
}
