using System;
using System.Text;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What <c>HOTKEYS START</c> puts on the wire, byte for byte - the operand carries every conditional
/// clause, so the shape is pinned here rather than discovered against a live server.
/// </summary>
public class HotKeysWireTests
{
    [Fact]
    public void DefaultsAskForBothMetricsAndNothingElse()
        => Assert.Equal(
            "*6|$7|HOTKEYS|$5|START|$7|METRICS|$1|2|$3|CPU|$3|NET|",
            Render(new((HotKeysMetrics)~0, count: 0, duration: default, sampleRatio: 1, slots: null)));

    [Fact]
    public void EveryClauseInServerOrder()
        => Assert.Equal(
            "*15|$7|HOTKEYS|$5|START|$7|METRICS|$1|1|$3|CPU|$5|COUNT|$2|10|" +
            "$8|DURATION|$2|30|$6|SAMPLE|$1|5|$5|SLOTS|$1|2|$1|1|$2|42|",
            Render(new(HotKeysMetrics.Cpu, count: 10, duration: TimeSpan.FromSeconds(30), sampleRatio: 5, slots: [1, 42])));

    [Fact]
    public void NetworkAloneCountsOne()
        => Assert.Equal(
            "*5|$7|HOTKEYS|$5|START|$7|METRICS|$1|1|$3|NET|",
            Render(new(HotKeysMetrics.Network, count: 0, duration: default, sampleRatio: 1, slots: null)));

    /// <summary>A partial second of duration still asks for a whole one: the operand rounds up, not down.</summary>
    [Fact]
    public void DurationRoundsUpToWholeSeconds()
        => Assert.Equal(
            "*8|$7|HOTKEYS|$5|START|$7|METRICS|$1|2|$3|CPU|$3|NET|$8|DURATION|$1|2|",
            Render(new((HotKeysMetrics)~0, count: 0, duration: TimeSpan.FromMilliseconds(1500), sampleRatio: 1, slots: null)));

    private static string Render(RedisServer.HotKeysStartOperand operand)
    {
        var ctx = new RespContext();
        using var frame = ctx.Render($"{RedisCommand.HOTKEYS}{operand}");
        return Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");
    }
}
