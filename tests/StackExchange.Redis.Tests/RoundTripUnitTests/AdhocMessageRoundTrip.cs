using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

public class AdHocMessageRoundTrip(ITestOutputHelper log)
{
    public enum MapMode
    {
        Null,
        Default,
        Disabled,
        Renamed,
    }

    [Theory(Timeout = 1000)]
    [InlineData(MapMode.Null, "", "*1\r\n$4\r\nECHO\r\n")]
    [InlineData(MapMode.Default, "", "*1\r\n$4\r\nECHO\r\n")]
    [InlineData(MapMode.Disabled, "", "")]
    [InlineData(MapMode.Renamed, "", "*1\r\n$5\r\nECHO2\r\n")]
    [InlineData(MapMode.Null, "hello", "*2\r\n$4\r\nECHO\r\n$5\r\nhello\r\n")]
    [InlineData(MapMode.Default, "hello", "*2\r\n$4\r\nECHO\r\n$5\r\nhello\r\n")]
    [InlineData(MapMode.Disabled, "hello", "")]
    [InlineData(MapMode.Renamed, "hello", "*2\r\n$5\r\nECHO2\r\n$5\r\nhello\r\n")]
    public async Task EchoRoundTripTest(MapMode mode, string payload, string requestResp)
    {
        var map = GetMap(mode);

        object[] args = string.IsNullOrEmpty(payload) ? [] : [payload];
        if (mode is MapMode.Disabled)
        {
            var ex = Assert.Throws<RedisCommandException>(() => new RedisDatabase.ExecuteMessage(map, -1, CommandFlags.None, "echo", args));
            Assert.StartsWith(ex.Message, "This operation has been disabled in the command-map and cannot be used: echo");
        }
        else
        {
            var msg = new RedisDatabase.ExecuteMessage(map, -1, CommandFlags.None, "echo", args);
            Assert.Equal(RedisCommand.ECHO, msg.Command); // in v3: this is recognized correctly

            Assert.Equal("ECHO", msg.CommandAndKey);
            Assert.Equal("ECHO", msg.CommandString);
            var result =
                await TestConnection.ExecuteAsync(msg, ResultProcessor.ScriptResult, requestResp, ":5\r\n", commandMap: map, log: log);
            Assert.Equal(ResultType.Integer, result.Resp3Type);
            Assert.Equal(5, result.AsInt32());
        }
    }

    private static CommandMap? GetMap(MapMode mode) => mode switch
    {
        MapMode.Null => null,
        MapMode.Default => CommandMap.Default,
        MapMode.Disabled => CommandMap.Create(new HashSet<string> { "echo", "custom" }, available: false),
        MapMode.Renamed => CommandMap.Create(new Dictionary<string, string?> { { "echo", "echo2" }, { "custom", "custom2" } }),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    [Theory(Timeout = 1000)]
    [InlineData(MapMode.Null, "", "*1\r\n$6\r\nCUSTOM\r\n")]
    [InlineData(MapMode.Default, "", "*1\r\n$6\r\nCUSTOM\r\n")]
    // [InlineData(MapMode.Disabled, "", "")]
    // [InlineData(MapMode.Renamed, "", "*1\r\n$7\r\nCUSTOM2\r\n")]
    [InlineData(MapMode.Null, "hello", "*2\r\n$6\r\nCUSTOM\r\n$5\r\nhello\r\n")]
    [InlineData(MapMode.Default, "hello", "*2\r\n$6\r\nCUSTOM\r\n$5\r\nhello\r\n")]
    // [InlineData(MapMode.Disabled, "hello", "")]
    // [InlineData(MapMode.Renamed, "hello", "*2\r\n$7\r\nCUSTOM2\r\n$5\r\nhello\r\n")]
    public async Task CustomRoundTripTest(MapMode mode, string payload, string requestResp)
    {
        var map = GetMap(mode);

        object[] args = string.IsNullOrEmpty(payload) ? [] : [payload];
        if (mode is MapMode.Disabled)
        {
            var ex = Assert.Throws<RedisCommandException>(() => new RedisDatabase.ExecuteMessage(map, -1, CommandFlags.None, "custom", args));
            Assert.StartsWith(ex.Message, "This operation has been disabled in the command-map and cannot be used: custom");
        }
        else
        {
            var msg = new RedisDatabase.ExecuteMessage(map, -1, CommandFlags.None, "custom", args);
            Assert.Equal(RedisCommand.UNKNOWN, msg.Command);

            Assert.Equal("custom", msg.CommandAndKey);
            Assert.Equal("custom", msg.CommandString);
            var result =
                await TestConnection.ExecuteAsync(msg, ResultProcessor.ScriptResult, requestResp, ":5\r\n", commandMap: map, log: log);
            Assert.Equal(ResultType.Integer, result.Resp3Type);
            Assert.Equal(5, result.AsInt32());
        }
    }
}
