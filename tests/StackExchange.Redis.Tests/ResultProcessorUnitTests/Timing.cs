using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

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
        var processor = ResultProcessor.ResponseTimer;
        var message = ResultProcessor.TimingProcessor.CreateMessage(-1, CommandFlags.None, RedisCommand.PING);
        var result = Execute(resp, processor, message);

        Assert.NotEqual(System.TimeSpan.MaxValue, result);
        Assert.True(result >= System.TimeSpan.Zero);
    }
}
