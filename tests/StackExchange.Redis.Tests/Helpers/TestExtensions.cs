using System.Linq;
using StackExchange.Redis.Profiling;
using Xunit;

namespace StackExchange.Redis.Tests;

public static class TestExtensions
{
    public static ProfilingSession AddProfiler(this IConnectionMultiplexer mutex)
    {
        var session = new ProfilingSession();
        mutex.RegisterProfiler(() => session);
        return session;
    }

    public static RedisProtocol GetProtocol(this ITestContext context) =>
        context.Test?.TestCase is ProtocolTestCase protocolTestCase
        ? protocolTestCase.Protocol : RedisProtocol.Resp2;

    public static bool IsResp2(this ITestContext context) => GetProtocol(context) == RedisProtocol.Resp2;
    public static bool IsResp3(this ITestContext context) => GetProtocol(context) == RedisProtocol.Resp3;

    public static string KeySuffix(this ITestContext context) => GetProtocol(context) switch
    {
        RedisProtocol.Resp2 => "R2",
        RedisProtocol.Resp3 => "R3",
        _ => "",
    };

    public static string GetString(this RedisProtocol protocol) => protocol switch
    {
        RedisProtocol.Resp2 => "RESP2",
        RedisProtocol.Resp3 => "RESP3",
        _ => "UnknownProtocolFixMeeeeee",
    };
}
