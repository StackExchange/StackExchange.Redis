using System;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Tests for TimeSpanProcessor
/// </summary>
public class TimeSpanTests(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData(":0\r\n", 0)]
    [InlineData(":1\r\n", 1)]
    [InlineData(":1000\r\n", 1000)]
    [InlineData(":60\r\n", 60)]
    [InlineData(":3600\r\n", 3600)]
    public void TimeSpanFromSeconds_ValidInteger(string resp, long seconds)
    {
        var processor = RespHandlers.TimeSpanFromSeconds;
        var result = Execute(resp, processor);
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(seconds), result.Value);
    }

    [Theory]
    [InlineData(":0\r\n", 0)]
    [InlineData(":1\r\n", 1)]
    [InlineData(":1000\r\n", 1000)]
    [InlineData(":60000\r\n", 60000)]
    [InlineData(":3600000\r\n", 3600000)]
    public void TimeSpanFromMilliseconds_ValidInteger(string resp, long milliseconds)
    {
        var processor = RespHandlers.Inbuilt<TimeSpan?>.Require();
        var result = Execute(resp, processor);
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), result.Value);
    }

    [Theory]
    [InlineData(":-1\r\n")]
    [InlineData(":-2\r\n")]
    [InlineData(":-100\r\n")]
    public void TimeSpanFromSeconds_NegativeInteger_ReturnsNull(string resp)
    {
        var processor = RespHandlers.TimeSpanFromSeconds;
        var result = Execute(resp, processor);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(":-1\r\n")]
    [InlineData(":-2\r\n")]
    [InlineData(":-100\r\n")]
    public void TimeSpanFromMilliseconds_NegativeInteger_ReturnsNull(string resp)
    {
        var processor = RespHandlers.Inbuilt<TimeSpan?>.Require();
        var result = Execute(resp, processor);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("$-1\r\n")] // RESP2 null bulk string
    [InlineData("_\r\n")] // RESP3 null
    public void TimeSpanFromSeconds_Null_ReturnsNull(string resp)
    {
        var processor = RespHandlers.TimeSpanFromSeconds;
        var result = Execute(resp, processor);
        Assert.Null(result);
    }

    // NEW BEHAVIOUR: a nil reply now fails rather than reading as null. This handler serves PTTL, which
    // answers -1/-2 (covered above) and never nil; the seconds handler keeps the nil case because
    // OBJECT IDLETIME does send it.
    [Theory]
    [InlineData("$-1\r\n")] // RESP2 null bulk string
    [InlineData("_\r\n")] // RESP3 null
    public void TimeSpanFromMilliseconds_Null_Fails(string resp)
    {
        var processor = RespHandlers.Inbuilt<TimeSpan?>.Require();
        ExecuteUnexpected(resp, processor);
    }

    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData("$2\r\nOK\r\n")]
    [InlineData("*2\r\n:1\r\n:2\r\n")]
    public void TimeSpanFromSeconds_InvalidType(string resp)
    {
        var processor = RespHandlers.TimeSpanFromSeconds;
        ExecuteUnexpected(resp, processor);
    }

    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData("$2\r\nOK\r\n")]
    [InlineData("*2\r\n:1\r\n:2\r\n")]
    public void TimeSpanFromMilliseconds_InvalidType(string resp)
    {
        var processor = RespHandlers.Inbuilt<TimeSpan?>.Require();
        ExecuteUnexpected(resp, processor);
    }
}
