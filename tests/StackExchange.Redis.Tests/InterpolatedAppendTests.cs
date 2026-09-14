using System;
using System.Text;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <c>cmd.Append($"...")</c>: a conditional fragment written the same way as the command.
/// </summary>
public partial class InterpolatedAppendTests
{
    private static readonly RespContext Ctx = new();

    private static string Text(in RespFrame frame) =>
        Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    internal static partial class RespLiterals
    {
#pragma warning disable SER011 // stands in for the generator
        internal static RespFragment EX => new("$2\r\nEX\r\n"u8);
#pragma warning restore SER011
    }

    [Theory]
    [InlineData(false, "*3|$3|SET|$1|k|$1|v|")]
    [InlineData(true, "*5|$3|SET|$1|k|$1|v|$2|EX|$3|300|")]
    public void ConditionalAppendMatchesTheUnconditionalForm(bool withTtl, string expected)
    {
        var cmd = Ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");
        if (withTtl) cmd.Append($"{RespLiterals.EX}{(RedisValue)300}");

        using var frame = Ctx.Execute(ref cmd);
        Assert.Equal(expected, Text(frame));
    }

    [Fact]
    public void AppendSurvivesABufferGrowth()
    {
        // the moved-in copy is what grows, swapping to a new pooled array - so if Append did not assign the
        // copy back, the command would still point at the OLD array, which Ensure has already returned to
        // the pool. This is the case that proves it is a move rather than a share.
        var big = new string('x', 4096);
        var cmd = Ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}");
        cmd.Append($"{(RedisValue)big}{(RedisValue)big}");

        using var frame = Ctx.Execute(ref cmd);
        var text = Text(frame);
        Assert.StartsWith("*4|$3|SET|$1|k|$4096|", text);
        Assert.Equal(4, frame.ArgCount);
    }

    [Fact]
    public void SeveralAppendsAccumulate()
    {
        var cmd = Ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");
        cmd.Append($"{RespLiterals.EX}{(RedisValue)300}");
        cmd.Append($"{(RedisValue)"XX"}");

        using var frame = Ctx.Execute(ref cmd);
        Assert.Equal("*6|$3|SET|$1|k|$1|v|$2|EX|$3|300|$2|XX|", Text(frame));
    }

    [Fact]
    public void KeysAppendedThisWayAreStillMarked()
    {
        var cmd = Ctx.Compose($"{RedisCommand.MGET}{(RedisKey)"a"}");
        cmd.Append($"{(RedisKey)"b"}");

        using var frame = Ctx.Execute(ref cmd);
        Assert.Equal(2, frame.KeyCount);
        var ranges = new KeyRange[2];
        Assert.Equal(2, frame.TryGetKeys(ranges));
        Assert.Equal("a", Encoding.UTF8.GetString(frame.GetKey(ranges[0]).ToArray()));
        Assert.Equal("b", Encoding.UTF8.GetString(frame.GetKey(ranges[1]).ToArray()));
    }
}
