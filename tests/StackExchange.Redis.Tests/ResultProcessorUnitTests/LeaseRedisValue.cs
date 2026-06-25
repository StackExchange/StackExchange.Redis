using System;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Tests for LeaseRedisValue result processor.
/// </summary>
public class LeaseRedisValue(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData("*0\r\n", 0)] // empty array (key doesn't exist)
    [InlineData("*1\r\n$5\r\nhello\r\n", 1)] // array with 1 element
    [InlineData("*3\r\n$3\r\naaa\r\n$3\r\nbbb\r\n$3\r\nccc\r\n", 3)] // array with 3 elements in lexicographical order
    [InlineData("*2\r\n$4\r\ntest\r\n$5\r\nvalue\r\n", 2)] // array with 2 elements
    [InlineData("*?\r\n$5\r\nhello\r\n$5\r\nworld\r\n.\r\n", 2)] // streaming aggregate with 2 elements
    [InlineData("*?\r\n.\r\n", 0)] // streaming empty array
    public void LeaseRedisValueProcessor_ValidInput(string resp, int expectedCount)
    {
        var processor = ResultProcessor.LeaseRedisValue;
        using var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(expectedCount, result.Length);
    }

    [Fact]
    public void LeaseRedisValueProcessor_ValidatesContent()
    {
        // Array of 3 RedisValues: "aaa", "bbb", "ccc"
        var resp = "*3\r\n$3\r\naaa\r\n$3\r\nbbb\r\n$3\r\nccc\r\n";
        var processor = ResultProcessor.LeaseRedisValue;
        using var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Equal("aaa", result.Span[0].ToString());
        Assert.Equal("bbb", result.Span[1].ToString());
        Assert.Equal("ccc", result.Span[2].ToString());
    }

    [Fact]
    public void LeaseRedisValueProcessor_EmptyArray()
    {
        // Empty array (key doesn't exist)
        var resp = "*0\r\n";
        var processor = ResultProcessor.LeaseRedisValue;
        using var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(0, result.Length);
    }

    [Theory]
    [InlineData("*-1\r\n")] // null array (RESP2)
    [InlineData("_\r\n")] // null (RESP3)
    public void LeaseRedisValueProcessor_NullArray(string resp)
    {
        var processor = ResultProcessor.LeaseRedisValue;
        var result = Execute(resp, processor);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("$5\r\nhello\r\n")] // scalar string (not an array)
    [InlineData(":42\r\n")] // scalar integer (not an array)
    [InlineData("+OK\r\n")] // simple string (not an array)
    public void LeaseRedisValueProcessor_InvalidInput(string resp)
    {
        var processor = ResultProcessor.LeaseRedisValue;
        ExecuteUnexpected(resp, processor);
    }

    [Fact]
    public void LeaseRedisValueProcessor_MixedTypes()
    {
        // Array with mixed types: bulk string, simple string, integer
        var resp = "*3\r\n$5\r\nhello\r\n+world\r\n:42\r\n";
        var processor = ResultProcessor.LeaseRedisValue;
        using var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Equal("hello", result.Span[0].ToString());
        Assert.Equal("world", result.Span[1].ToString());
        Assert.Equal("42", result.Span[2].ToString());
    }
}
