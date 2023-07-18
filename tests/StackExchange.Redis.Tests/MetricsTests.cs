#if NET6_0_OR_GREATER
#pragma warning disable TBD // MetricCollector is for evaluation purposes only and is subject to change or removal in future updates.

using Microsoft.Extensions.Telemetry.Testing.Metering;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class MetricsTests : TestBase
{
    public MetricsTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task SimpleCommandDuration()
    {
        var options = ConfigurationOptions.Parse(GetConfiguration());

        using var meter = new Meter("StackExchange.Redis.Tests");
        using var collector = new MetricCollector<double>(meter, "db.redis.duration");

        options.MeterFactory = () => meter;

        using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);
        var db = conn.GetDatabase();

        RedisKey key = Me();
        string? g1 = await db.StringGetAsync(key);
        Assert.Null(g1);

        await collector.WaitForMeasurementsAsync(1);

        Assert.Collection(collector.GetMeasurementSnapshot(),
            measurement =>
            {
                // Built-in
                Assert.Equal(true, measurement.Tags["db.redis.async"]);
                Assert.Equal(false, measurement.Tags["db.redis.faulted"]);
            });
    }
}
#endif
