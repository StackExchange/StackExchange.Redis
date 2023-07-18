#if NET6_0_OR_GREATER

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    internal RedisMetrics Metrics { get; }

    private static RedisMetrics GetMetrics(ConfigurationOptions configuration)
    {
        if (configuration.MeterFactory is not null)
        {
            return new RedisMetrics(configuration.MeterFactory());
        }

        return RedisMetrics.Default;
    }
}
#endif
