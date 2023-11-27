using StackExchange.Redis.Profiling;

namespace StackExchange.Redis.Tests;

public static class TestExtensions
{
    public static ProfilingSession AddProfiler(this IConnectionMultiplexer mutex)
    {
        var session = new ProfilingSession();
        mutex.RegisterProfiler(() => session);
        return session;
    }
}
