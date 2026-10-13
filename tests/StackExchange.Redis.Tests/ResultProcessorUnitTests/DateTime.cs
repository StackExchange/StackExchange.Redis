using System;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Tests for DateTime result processors
/// </summary>
public class DateTimeTests(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Theory]
    [InlineData(":1609459200\r\n")] // scalar integer (Jan 1, 2021 00:00:00 UTC)
    [InlineData("*1\r\n:1609459200\r\n")] // array of 1 (seconds only)
    [InlineData("*?\r\n:1609459200\r\n.\r\n")] // streaming aggregate of 1
    [InlineData(ATTRIB_FOO_BAR + ":1609459200\r\n")]
    public void DateTimeWithoutMicrosecondsFails(string resp)
    {
        // NEW BEHAVIOUR: these shapes used to read as whole seconds; the TIME handler demands exactly
        // [seconds, microseconds], which is all TIME has ever replied. The general-purpose DateTime
        // processor they exercised has no other caller left (LASTSAVE reads an Int64 and converts inline).
        ExecuteUnexpected(resp, Diagnostics.ServerTimeHandler.Instance);
    }

    [Theory]
    [InlineData("*2\r\n:1609459200\r\n:500000\r\n")] // array of 2 (seconds + microseconds)
    [InlineData("*?\r\n:1609459200\r\n:500000\r\n.\r\n")] // streaming aggregate of 2
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n:1609459200\r\n:500000\r\n")]
    public void DateTime(string resp)
    {
        // 500000 microseconds = 0.5 seconds = 5000000 ticks (100ns each)
        var expected = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(5000000);
        Assert.Equal(expected, Execute(resp, Diagnostics.ServerTimeHandler.Instance));
    }

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*?\r\n.\r\n")] // streaming empty aggregate
    [InlineData("*3\r\n:1\r\n:2\r\n:3\r\n")] // array with 3 elements
    [InlineData("$5\r\nhello\r\n")] // bulk string
    public void FailingDateTime(string resp) => ExecuteUnexpected(resp, Diagnostics.ServerTimeHandler.Instance);

    // NullableDateTimeFromSeconds had no counterpart to retarget: nothing in the new core reads a seconds
    // reply as a DateTime? (LASTSAVE reads an Int64 and converts inline; expiry reads PEXPIRETIME, in ms),
    // so its tests went with it.
    [Theory]
    [InlineData(":1609459200000\r\n")] // positive value (Jan 1, 2021 00:00:00 UTC) - milliseconds
    [InlineData(",1609459200000\r\n")] // RESP3 number
    [InlineData(ATTRIB_FOO_BAR + ":1609459200000\r\n")]
    public void NullableDateTimeFromMilliseconds(string resp)
    {
        var expected = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, Execute(resp, RespHandlers.Inbuilt<DateTime?>.Require()));
    }

    [Theory]
    [InlineData(":-1\r\n", null)] // -1 means no expiry
    [InlineData(":-2\r\n", null)] // -2 means key does not exist
    public void NullableDateTimeNull(string resp, DateTime? expected)
    {
        Assert.Equal(expected, Execute(resp, RespHandlers.Inbuilt<DateTime?>.Require()));
    }

    // NEW BEHAVIOUR: a nil reply now fails rather than reading as null. The handler serves PEXPIRETIME,
    // which answers -1/-2 (above) and never nil.
    [Theory]
    [InlineData("_\r\n")] // RESP3 null
    [InlineData("$-1\r\n")] // RESP2 null bulk string
    public void NullableDateTimeNilFails(string resp) => ExecuteUnexpected(resp, RespHandlers.Inbuilt<DateTime?>.Require());

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n:1\r\n:2\r\n")] // array
    public void FailingNullableDateTime(string resp)
    {
        ExecuteUnexpected(resp, RespHandlers.Inbuilt<DateTime?>.Require());
    }
}
