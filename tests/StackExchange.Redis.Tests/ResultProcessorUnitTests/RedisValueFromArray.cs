using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class RedisValueFromArray(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void SingleElementArray_String()
    {
        var result = Execute("*1\r\n$5\r\nhello\r\n", RespHandlers.SingletonValue);
        Assert.Equal("hello", (string?)result);
    }

    [Fact]
    public void SingleElementArray_Integer()
    {
        var result = Execute("*1\r\n:42\r\n", RespHandlers.SingletonValue);
        Assert.Equal(42, (long)result);
    }

    [Fact]
    public void SingleElementArray_Null()
    {
        var result = Execute("*1\r\n$-1\r\n", RespHandlers.SingletonValue);
        Assert.True(result.IsNull);
    }

    [Fact]
    public void SingleElementArray_EmptyString()
    {
        var result = Execute("*1\r\n$0\r\n\r\n", RespHandlers.SingletonValue);
        Assert.Equal("", (string?)result);
    }
}
