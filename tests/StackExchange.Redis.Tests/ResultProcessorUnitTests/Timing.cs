using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// <c>PING</c>, timed: the shipped <c>TimingProcessor</c> is now the handler behind <c>PingMeasureAsync</c>
/// (and so <c>IDatabase.Ping</c>), which is created just before the send and reads the clock on the reply.
/// </summary>
public class Timing(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData(":42\r\n")]
    [InlineData(":0\r\n")]
    [InlineData(":-1\r\n")]
    [InlineData("$5\r\nhello\r\n")]
    [InlineData("$0\r\n\r\n")]
    [InlineData("$-1\r\n")]
    [InlineData("*2\r\n:1\r\n:2\r\n")]
    [InlineData("*0\r\n")]
    [InlineData("_\r\n")]
    public void Timing_ValidResponse_ReturnsTimeSpan(string resp)
    {
        var result = Execute(resp, new RespSurface.PingMeasureHandler());

        Assert.NotEqual(System.TimeSpan.MaxValue, result);
        Assert.True(result >= System.TimeSpan.Zero);
    }
}
