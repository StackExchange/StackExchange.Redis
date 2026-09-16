using System;
using System.Text;
using StackExchange.Redis.Interpolated;
using Xunit;
using static StackExchange.Redis.Tests.RespLiterals;

namespace StackExchange.Redis.Tests;

/// <summary>Fixed tokens, declared once at namespace scope so they can be imported.</summary>
internal static partial class RespLiterals
{
    /// <summary>The <c>NX</c> option.</summary>
    [Resp]
    internal static partial RespFragment Nx { get; }

    /// <summary>The <c>EX</c> option.</summary>
    [Resp]
    internal static partial RespFragment Ex { get; }

    /// <summary>The <c>GET</c> subcommand, of <c>CONFIG GET</c> and friends.</summary>
    [Resp("GET")]
    internal static partial RespFragment Get { get; }
}

/// <summary>
/// Whether <c>using static</c> closes the ergonomic gap that made inline literal tokens tempting.
/// </summary>
public class InterpolatedWriterUsingStaticTests
{
    private static string Frame(in RespRequestFrame frame) => Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    [Fact]
    public void ImportedFragmentsReadAlmostLikeInlineTokens()
    {
        // $"{key} {Nx} {value}" against the inline form it replaces, $"{key} nx {value}"
        var ctx = new RespContext();
        using var frame = ctx.Render(RedisCommand.SET, $"{(RedisKey)"k"} {(RedisValue)"v"} {Nx} {Ex} {(RedisValue)300}");

        Assert.Equal("*6|$3|SET|$1|k|$1|v|$2|NX|$2|EX|$3|300|", Frame(frame));
        Assert.Equal(6, frame.ArgCount);
    }

    [Fact]
    public void ImportedAndQualifiedAreTheSame()
    {
        var ctx = new RespContext();
        using var imported = ctx.Render(RedisCommand.CONFIG, $"{Get} {(RedisValue)"maxmemory"}");
        using var qualified = ctx.Render(RedisCommand.CONFIG, $"{RespLiterals.Get} {(RedisValue)"maxmemory"}");

        Assert.True(imported.Span.SequenceEqual(qualified.Span));
        Assert.Equal("*3|$6|CONFIG|$3|GET|$9|maxmemory|", Frame(imported));
    }
}
