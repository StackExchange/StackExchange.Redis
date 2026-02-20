using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class DemandZeroOrOne(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData(":0\r\n", false)]
    [InlineData(":1\r\n", true)]
    [InlineData("+0\r\n", false)]
    [InlineData("+1\r\n", true)]
    [InlineData("$1\r\n0\r\n", false)]
    [InlineData("$1\r\n1\r\n", true)]
    public void ValidZeroOrOne_Success(string resp, bool expected)
    {
        var result = Execute(resp, ResultProcessor.DemandZeroOrOne);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(":2\r\n")]
    [InlineData("+OK\r\n")]
    [InlineData("*1\r\n:1\r\n")]
    [InlineData("$-1\r\n")]
    public void InvalidResponse_Failure(string resp)
    {
        ExecuteUnexpected(resp, ResultProcessor.DemandZeroOrOne);
    }
}
