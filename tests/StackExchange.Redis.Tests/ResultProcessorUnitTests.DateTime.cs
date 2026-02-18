using System;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for DateTime result processors
/// </summary>
public partial class ResultProcessorUnitTests
{
    [Theory]
    [InlineData(":1609459200\r\n")] // scalar integer (Jan 1, 2021 00:00:00 UTC)
    [InlineData("*1\r\n:1609459200\r\n")] // array of 1 (seconds only)
    [InlineData("*?\r\n:1609459200\r\n.\r\n")] // streaming aggregate of 1
    [InlineData(ATTRIB_FOO_BAR + ":1609459200\r\n")]
    public void DateTime(string resp)
    {
        var expected = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, Execute(resp, ResultProcessor.DateTime));
    }

    [Theory]
    [InlineData("*2\r\n:1609459200\r\n:500000\r\n")] // array of 2 (seconds + microseconds)
    [InlineData("*?\r\n:1609459200\r\n:500000\r\n.\r\n")] // streaming aggregate of 2
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n:1609459200\r\n:500000\r\n")]
    public void DateTimeWithMicroseconds(string resp)
    {
        // 500000 microseconds = 0.5 seconds = 5000000 ticks (100ns each)
        var expected = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(5000000);
        Assert.Equal(expected, Execute(resp, ResultProcessor.DateTime));
    }

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*?\r\n.\r\n")] // streaming empty aggregate
    [InlineData("*3\r\n:1\r\n:2\r\n:3\r\n")] // array with 3 elements
    [InlineData("$5\r\nhello\r\n")] // bulk string
    public void FailingDateTime(string resp) => ExecuteUnexpected(resp, ResultProcessor.DateTime);

    [Theory]
    [InlineData(":1609459200\r\n")] // positive value (Jan 1, 2021 00:00:00 UTC) - seconds
    [InlineData(",1609459200\r\n")] // RESP3 number
    [InlineData(ATTRIB_FOO_BAR + ":1609459200\r\n")]
    public void NullableDateTimeFromSeconds(string resp)
    {
        var expected = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, Execute(resp, ResultProcessor.NullableDateTimeFromSeconds));
    }

    [Theory]
    [InlineData(":1609459200000\r\n")] // positive value (Jan 1, 2021 00:00:00 UTC) - milliseconds
    [InlineData(",1609459200000\r\n")] // RESP3 number
    [InlineData(ATTRIB_FOO_BAR + ":1609459200000\r\n")]
    public void NullableDateTimeFromMilliseconds(string resp)
    {
        var expected = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, Execute(resp, ResultProcessor.NullableDateTimeFromMilliseconds));
    }

    [Theory]
    [InlineData(":-1\r\n", null)] // -1 means no expiry
    [InlineData(":-2\r\n", null)] // -2 means key does not exist
    [InlineData("_\r\n", null)] // RESP3 null
    [InlineData("$-1\r\n", null)] // RESP2 null bulk string
    public void NullableDateTimeNull(string resp, DateTime? expected)
    {
        Assert.Equal(expected, Execute(resp, ResultProcessor.NullableDateTimeFromSeconds));
        Assert.Equal(expected, Execute(resp, ResultProcessor.NullableDateTimeFromMilliseconds));
    }

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n:1\r\n:2\r\n")] // array
    public void FailingNullableDateTime(string resp)
    {
        ExecuteUnexpected(resp, ResultProcessor.NullableDateTimeFromSeconds);
        ExecuteUnexpected(resp, ResultProcessor.NullableDateTimeFromMilliseconds);
    }
}
