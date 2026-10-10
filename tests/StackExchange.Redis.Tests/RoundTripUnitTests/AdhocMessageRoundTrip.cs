using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// The object-argument <c>Execute</c>: a known command name goes through the command map, an unknown one
/// is sent as written.
/// </summary>
public class AdHocMessageRoundTrip(ITestOutputHelper log)
{
    public enum MapMode
    {
        Null,
        Default,
        Disabled,
        Renamed,
    }

    [Theory(Timeout = 10000)]
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
            var executor = new RoundTripExecutor(":5\r\n");
            var db = RoundTrip.Database(executor, map);
            var ex = Assert.Throws<RedisCommandException>(() => db.Execute("echo", args));
            Assert.StartsWith("This operation has been disabled in the command-map and cannot be used: ", ex.Message);
            Assert.Contains("ECHO", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(executor.Frames);
        }
        else
        {
            // a known name is recognised, so the map's rename applies
            var result = await RoundTrip.ExecuteAsync(db => db.ExecuteAsync("echo", args), requestResp, ":5\r\n", commandMap: map, log: log);
            Assert.Equal(ResultType.Integer, result.Resp3Type);
            Assert.Equal(5, result.AsInt32());
        }
    }

    [Theory(Timeout = 10000)]
    [InlineData("ACL SETUSER x")]
    [InlineData("get key")]
    public void CommandWithWhitespaceThrows(string command)
    {
        var executor = new RoundTripExecutor("+OK\r\n");
        var db = RoundTrip.Database(executor);
        var ex = Assert.Throws<RedisCommandException>(() => db.Execute(command));
        Assert.Contains("whitespace", ex.Message);
        Assert.Empty(executor.Frames);
    }

    [Fact(Timeout = 10000)]
    public async Task SingleTokenCommandDoesNotThrow()
    {
        // the correct token-per-argument form must still be accepted unchanged
        var result = await RoundTrip.ExecuteAsync(
            db => db.ExecuteAsync("ACL", "SETUSER", "x"),
            "*3\r\n$3\r\nACL\r\n$7\r\nSETUSER\r\n$1\r\nx\r\n",
            "+OK\r\n",
            log: log);
        Assert.Equal("OK", (string?)result);
    }

    private static CommandMap? GetMap(MapMode mode) => mode switch
    {
        MapMode.Null => null,
        MapMode.Default => CommandMap.Default,
        MapMode.Disabled => CommandMap.Create(new HashSet<string> { "echo", "custom" }, available: false),
        MapMode.Renamed => CommandMap.Create(new Dictionary<string, string?> { { "echo", "echo2" }, { "custom", "custom2" } }),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    [Theory(Timeout = 10000)]
    [InlineData(MapMode.Null, "", "*1\r\n$6\r\ncustom\r\n")]
    [InlineData(MapMode.Default, "", "*1\r\n$6\r\ncustom\r\n")]
    // [InlineData(MapMode.Disabled, "", "")]
    // [InlineData(MapMode.Renamed, "", "*1\r\n$7\r\nCUSTOM2\r\n")]
    [InlineData(MapMode.Null, "hello", "*2\r\n$6\r\ncustom\r\n$5\r\nhello\r\n")]
    [InlineData(MapMode.Default, "hello", "*2\r\n$6\r\ncustom\r\n$5\r\nhello\r\n")]
    // [InlineData(MapMode.Disabled, "hello", "")]
    // [InlineData(MapMode.Renamed, "hello", "*2\r\n$7\r\nCUSTOM2\r\n$5\r\nhello\r\n")]
    public async Task CustomRoundTripTest(MapMode mode, string payload, string requestResp)
    {
        var map = GetMap(mode);

        object[] args = string.IsNullOrEmpty(payload) ? [] : [payload];

        // an unknown name is not a RedisCommand, so the map has nothing to say about it. The old core upper-cased
        // it ("CUSTOM"); the new one sends the name exactly as written, which is equally valid because command
        // names are case-insensitive server-side
        var result = await RoundTrip.ExecuteAsync(db => db.ExecuteAsync("custom", args), requestResp, ":5\r\n", commandMap: map, log: log);
        Assert.Equal(ResultType.Integer, result.Resp3Type);
        Assert.Equal(5, result.AsInt32());
    }
}
