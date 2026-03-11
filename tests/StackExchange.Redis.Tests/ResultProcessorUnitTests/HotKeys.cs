using System;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class HotKeys(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void FullFormat_Success()
    {
        // HOTKEYS GET - full response with all fields
        // Carefully counted byte lengths for each string
        var resp = "*1\r\n" +
                   "*24\r\n" +
                   "$15\r\ntracking-active\r\n" +
                   ":0\r\n" +
                   "$12\r\nsample-ratio\r\n" +
                   ":1\r\n" +
                   "$14\r\nselected-slots\r\n" +
                   "*1\r\n" +
                   "*2\r\n" +
                   ":0\r\n" +
                   ":16383\r\n" +
                   "$25\r\nall-commands-all-slots-us\r\n" +
                   ":103\r\n" +
                   "$32\r\nnet-bytes-all-commands-all-slots\r\n" +
                   ":2042\r\n" +
                   "$29\r\ncollection-start-time-unix-ms\r\n" +
                   ":1770824933147\r\n" +
                   "$22\r\ncollection-duration-ms\r\n" +
                   ":0\r\n" +
                   "$22\r\ntotal-cpu-time-user-ms\r\n" +
                   ":23\r\n" +
                   "$21\r\ntotal-cpu-time-sys-ms\r\n" +
                   ":7\r\n" +
                   "$15\r\ntotal-net-bytes\r\n" +
                   ":2038\r\n" +
                   "$14\r\nby-cpu-time-us\r\n" +
                   "*10\r\n" +
                   "$18\r\nhotkey_001_counter\r\n" +
                   ":29\r\n" +
                   "$10\r\nhotkey_001\r\n" +
                   ":25\r\n" +
                   "$15\r\nhotkey_001_hash\r\n" +
                   ":11\r\n" +
                   "$15\r\nhotkey_001_list\r\n" +
                   ":9\r\n" +
                   "$14\r\nhotkey_001_set\r\n" +
                   ":9\r\n" +
                   "$12\r\nby-net-bytes\r\n" +
                   "*10\r\n" +
                   "$10\r\nhotkey_001\r\n" +
                   ":446\r\n" +
                   "$10\r\nhotkey_002\r\n" +
                   ":328\r\n" +
                   "$15\r\nhotkey_001_hash\r\n" +
                   ":198\r\n" +
                   "$14\r\nhotkey_001_set\r\n" +
                   ":167\r\n" +
                   "$18\r\nhotkey_001_counter\r\n" +
                   ":116\r\n";

        var result = Execute(resp, HotKeysResult.Processor);

        Assert.NotNull(result);
        Assert.False(result.TrackingActive);
        Assert.Equal(1, result.SampleRatio);
        Assert.Equal(103, result.AllCommandsAllSlotsMicroseconds);
        Assert.Equal(2042, result.AllCommandsAllSlotsNetworkBytes);
        Assert.Equal(1770824933147, result.CollectionStartTimeUnixMilliseconds);
        Assert.Equal(0, result.CollectionDurationMicroseconds);
        Assert.Equal(23000, result.TotalCpuTimeUserMicroseconds);
        Assert.Equal(7000, result.TotalCpuTimeSystemMicroseconds);
        Assert.Equal(2038, result.TotalNetworkBytes);

        // Validate TimeSpan properties
        // 103 microseconds = 0.103 milliseconds
        Assert.Equal(0.103, result.AllCommandsAllSlotsTime.TotalMilliseconds, precision: 10);
        Assert.Equal(TimeSpan.Zero, result.CollectionDuration);
        // 23000 microseconds = 23 milliseconds
        Assert.Equal(23.0, result.TotalCpuTimeUser!.Value.TotalMilliseconds, precision: 10);
        // 7000 microseconds = 7 milliseconds
        Assert.Equal(7.0, result.TotalCpuTimeSystem!.Value.TotalMilliseconds, precision: 10);
        // 30000 microseconds = 30 milliseconds
        Assert.Equal(30.0, result.TotalCpuTime!.Value.TotalMilliseconds, precision: 10);

        // Validate by-cpu-time-us array
        Assert.Equal(5, result.CpuByKey.Length);
        Assert.Equal("hotkey_001_counter", (string?)result.CpuByKey[0].Key);
        Assert.Equal(29, result.CpuByKey[0].DurationMicroseconds);
        Assert.Equal("hotkey_001", (string?)result.CpuByKey[1].Key);
        Assert.Equal(25, result.CpuByKey[1].DurationMicroseconds);
        Assert.Equal("hotkey_001_hash", (string?)result.CpuByKey[2].Key);
        Assert.Equal(11, result.CpuByKey[2].DurationMicroseconds);
        Assert.Equal("hotkey_001_list", (string?)result.CpuByKey[3].Key);
        Assert.Equal(9, result.CpuByKey[3].DurationMicroseconds);
        Assert.Equal("hotkey_001_set", (string?)result.CpuByKey[4].Key);
        Assert.Equal(9, result.CpuByKey[4].DurationMicroseconds);

        // Validate by-net-bytes array
        Assert.Equal(5, result.NetworkBytesByKey.Length);
        Assert.Equal("hotkey_001", (string?)result.NetworkBytesByKey[0].Key);
        Assert.Equal(446, result.NetworkBytesByKey[0].Bytes);
        Assert.Equal("hotkey_002", (string?)result.NetworkBytesByKey[1].Key);
        Assert.Equal(328, result.NetworkBytesByKey[1].Bytes);
        Assert.Equal("hotkey_001_hash", (string?)result.NetworkBytesByKey[2].Key);
        Assert.Equal(198, result.NetworkBytesByKey[2].Bytes);
        Assert.Equal("hotkey_001_set", (string?)result.NetworkBytesByKey[3].Key);
        Assert.Equal(167, result.NetworkBytesByKey[3].Bytes);
        Assert.Equal("hotkey_001_counter", (string?)result.NetworkBytesByKey[4].Key);
        Assert.Equal(116, result.NetworkBytesByKey[4].Bytes);
    }

    [Fact]
    public void MinimalFormat_Success()
    {
        // Minimal HOTKEYS response with just tracking-active
        var resp = "*1\r\n" +
                   "*2\r\n" +
                   "$15\r\ntracking-active\r\n" +
                   ":1\r\n";

        var result = Execute(resp, HotKeysResult.Processor);

        Assert.NotNull(result);
        Assert.True(result.TrackingActive);
    }

    [Fact]
    public void NotArray_Failure()
    {
        var resp = "$5\r\nhello\r\n";

        ExecuteUnexpected(resp, HotKeysResult.Processor);
    }

    [Fact]
    public void Null_Success()
    {
        var resp = "$-1\r\n";

        var result = Execute(resp, HotKeysResult.Processor);
        Assert.Null(result);
    }
}
