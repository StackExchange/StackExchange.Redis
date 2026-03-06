using System;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Tests for Lease result processors.
/// </summary>
public class Lease(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData("*0\r\n", 0)] // empty array
    [InlineData("*3\r\n,1.5\r\n,2.5\r\n,3.5\r\n", 3)] // 3 floats
    [InlineData("*2\r\n:1\r\n:2\r\n", 2)] // integers converted to floats
    [InlineData("*1\r\n$3\r\n1.5\r\n", 1)] // bulk string converted to float
    [InlineData("*?\r\n,1.5\r\n,2.5\r\n,3.5\r\n.\r\n", 3)] // streaming aggregate with 3 floats
    [InlineData("*?\r\n.\r\n", 0)] // streaming empty array
    public void LeaseFloat32Processor_ValidInput(string resp, int expectedCount)
    {
        var processor = ResultProcessor.LeaseFloat32;
        using var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(expectedCount, result.Length);
    }

    [Fact]
    public void LeaseFloat32Processor_ValidatesContent()
    {
        // Array of 3 floats: 1.5, 2.5, 3.5
        var resp = "*3\r\n,1.5\r\n,2.5\r\n,3.5\r\n";
        var processor = ResultProcessor.LeaseFloat32;
        using var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Equal(1.5f, result.Span[0]);
        Assert.Equal(2.5f, result.Span[1]);
        Assert.Equal(3.5f, result.Span[2]);
    }

    [Theory]
    [InlineData("*-1\r\n")] // null array (RESP2)
    [InlineData("_\r\n")] // null (RESP3)
    public void LeaseFloat32Processor_NullArray(string resp)
    {
        var processor = ResultProcessor.LeaseFloat32;
        var result = Execute(resp, processor);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("$5\r\nhello\r\n")] // scalar string (not an array)
    [InlineData(":42\r\n")] // scalar integer (not an array)
    public void LeaseFloat32Processor_InvalidInput(string resp)
    {
        var processor = ResultProcessor.LeaseFloat32;
        ExecuteUnexpected(resp, processor);
    }

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")] // bulk string
    [InlineData("+world\r\n", "world")] // simple string
    [InlineData(":42\r\n", "42")] // integer
    public void LeaseProcessor_ValidInput(string resp, string expected)
    {
        var processor = ResultProcessor.Lease;
        using var result = Execute(resp, processor);

        Assert.NotNull(result);

        var str = Encoding.UTF8.GetString(result.Span);
        Assert.Equal(expected, str);
    }

    [Theory]
    [InlineData("*1\r\n$5\r\nhello\r\n", "hello")] // array of 1 bulk string
    [InlineData("*1\r\n+world\r\n", "world")] // array of 1 simple string
    public void LeaseFromArrayProcessor_ValidInput(string resp, string expected)
    {
        var processor = ResultProcessor.LeaseFromArray;
        using var result = Execute(resp, processor);

        Assert.NotNull(result);

        var str = Encoding.UTF8.GetString(result.Span);
        Assert.Equal(expected, str);
    }

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$5\r\nhello\r\n$5\r\nworld\r\n")] // array of 2 (not 1)
    public void LeaseFromArrayProcessor_InvalidInput(string resp)
    {
        var processor = ResultProcessor.LeaseFromArray;
        ExecuteUnexpected(resp, processor);
    }
}
