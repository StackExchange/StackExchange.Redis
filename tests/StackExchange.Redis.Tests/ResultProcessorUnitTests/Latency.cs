using System;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Tests for Latency result processors.
/// </summary>
public class Latency(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    // Diagnostics.LatencyHandler, over the shipped LatencyLatestEntry/LatencyHistoryEntry.TryParseEntry
    private static IRespHandler<LatencyLatestEntry[]> Latest
        => NewCoreHandlers.Get<LatencyLatestEntry[]>(typeof(Diagnostics), "LatencyHandler", "Latest");

    private static IRespHandler<LatencyHistoryEntry[]> History
        => NewCoreHandlers.Get<LatencyHistoryEntry[]>(typeof(Diagnostics), "LatencyHandler", "History");

    [Theory]
    [InlineData("*0\r\n", 0)] // empty array
    [InlineData("*1\r\n*4\r\n$7\r\ncommand\r\n:1405067976\r\n:251\r\n:1001\r\n", 1)] // single entry
    [InlineData("*2\r\n*4\r\n$7\r\ncommand\r\n:1405067976\r\n:251\r\n:1001\r\n*4\r\n$4\r\nfast\r\n:1405067980\r\n:100\r\n:500\r\n", 2)] // two entries
    public void LatencyLatestEntry_ValidInput(string resp, int expectedCount)
    {
        var processor = Latest;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(expectedCount, result.Length);
    }

    [Fact]
    public void LatencyLatestEntry_ValidatesContent()
    {
        // Single entry: ["command", 1405067976, 251, 1001]
        var resp = "*1\r\n*4\r\n$7\r\ncommand\r\n:1405067976\r\n:251\r\n:1001\r\n";
        var processor = Latest;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Single(result);

        var entry = result[0];
        Assert.Equal("command", entry.EventName);
        Assert.Equal(RedisBase.UnixEpoch.AddSeconds(1405067976), entry.Timestamp);
        Assert.Equal(251, entry.DurationMilliseconds);
        Assert.Equal(1001, entry.MaxDurationMilliseconds);
    }

    [Theory]
    [InlineData("*-1\r\n")] // null array (RESP2)
    [InlineData("_\r\n")] // null (RESP3)
    public void LatencyLatestEntry_NullArray(string resp)
    {
        // BEHAVIOUR CHANGE: the shipped ToArray processor answered null for a nil reply; the new core's
        // LatencyHandler answers an empty array (ReadPastArray(...) ?? []), matching the non-nullable
        // array the IServer surface promises.
        var processor = Latest;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("*0\r\n", 0)] // empty array
    [InlineData("*1\r\n*2\r\n:1405067822\r\n:251\r\n", 1)] // single entry
    [InlineData("*2\r\n*2\r\n:1405067822\r\n:251\r\n*2\r\n:1405067941\r\n:1001\r\n", 2)] // two entries (from redis-cli example)
    public void LatencyHistoryEntry_ValidInput(string resp, int expectedCount)
    {
        var processor = History;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(expectedCount, result.Length);
    }

    [Fact]
    public void LatencyHistoryEntry_ValidatesContent()
    {
        // Two entries from redis-cli example
        var resp = "*2\r\n*2\r\n:1405067822\r\n:251\r\n*2\r\n:1405067941\r\n:1001\r\n";
        var processor = History;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);

        var entry1 = result[0];
        Assert.Equal(RedisBase.UnixEpoch.AddSeconds(1405067822), entry1.Timestamp);
        Assert.Equal(251, entry1.DurationMilliseconds);

        var entry2 = result[1];
        Assert.Equal(RedisBase.UnixEpoch.AddSeconds(1405067941), entry2.Timestamp);
        Assert.Equal(1001, entry2.DurationMilliseconds);
    }

    [Theory]
    [InlineData("*-1\r\n")] // null array (RESP2)
    [InlineData("_\r\n")] // null (RESP3)
    public void LatencyHistoryEntry_NullArray(string resp)
    {
        // BEHAVIOUR CHANGE: the shipped ToArray processor answered null for a nil reply; the new core's
        // LatencyHandler answers an empty array (ReadPastArray(...) ?? []), matching the non-nullable
        // array the IServer surface promises.
        var processor = History;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
