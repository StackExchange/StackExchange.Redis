using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class SentinelAbortOnConnectTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public void ConnectToUnreachableSentinelReturnsDisconnectedMultiplexer()
    {
        var config = ConfigurationOptions.Parse("nonexistent:26379,serviceName=mymaster");
        config.AbortOnConnectFail = false;
        config.ConnectTimeout = 1000;

        using var mux = ConnectionMultiplexer.Connect(config);
        Assert.NotNull(mux);
        Assert.False(mux.IsConnected);
    }

    [Fact]
    public async Task ConnectAsyncToUnreachableSentinelReturnsDisconnectedMultiplexer()
    {
        var config = ConfigurationOptions.Parse("nonexistent:26379,serviceName=mymaster");
        config.AbortOnConnectFail = false;
        config.ConnectTimeout = 1000;

        await using var mux = await ConnectionMultiplexer.ConnectAsync(config);
        Assert.NotNull(mux);
        Assert.False(mux.IsConnected);
    }

    [Fact]
    public void ConnectToUnreachableSentinelThrowsWhenAbortOnConnectFailIsTrue()
    {
        var config = ConfigurationOptions.Parse("nonexistent:26379,serviceName=mymaster");
        config.AbortOnConnectFail = true;
        config.ConnectTimeout = 1000;

        Assert.Throws<RedisConnectionException>(() => ConnectionMultiplexer.Connect(config));
    }
}
