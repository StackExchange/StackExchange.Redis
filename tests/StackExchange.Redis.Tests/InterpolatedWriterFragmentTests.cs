using System;
using System.Text;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Shows how fixed RESP tokens would be authored: a declared partial property, and the body a generator
/// would emit. Both halves are hand-written here - the point is the usage, not the generator.
/// See design/interpolated-resp-writer.md section 2.3.
/// </summary>
public class InterpolatedWriterFragmentTests
{
    // ---- half 1: what the AUTHOR writes -----------------------------------------------------------
    // The attribute is only needed when the token differs from the member name, or when the fragment
    // spans more than one token.

    internal static partial class RespLiterals
    {
        /// <summary>The <c>EX</c> option of SET. Token inferred from the member name.</summary>
        [Resp]
        internal static partial RespFragment EX { get; }

        /// <summary>The subcommand of <c>CONFIG GET</c> - one argument, name would not suffice.</summary>
        [Resp("GET")]
        internal static partial RespFragment ConfigGet { get; }

        /// <summary>
        /// <c>SETINFO lib-name</c>, the two arguments following <c>CLIENT</c>. Note the mixed casing: the
        /// subcommand is a keyword and upper-cased, the attribute name is a value and is not - which is
        /// what RedisLiterals sends today, and why tokens given in the attribute are taken verbatim.
        /// </summary>
        [Resp("SETINFO", "lib-name")]
        internal static partial RespFragment SetInfoLibName { get; }

        /// <summary><c>MAXLEN ~</c>, the two arguments preceding an XADD/XTRIM threshold.</summary>
        [Resp("MAXLEN", "~")]
        internal static partial RespFragment MaxLenApprox { get; }

        /// <summary><c>LEFT RIGHT</c>, the fixed pair ending an <c>LMOVE</c>.</summary>
        [Resp("LEFT", "RIGHT")]
        internal static partial RespFragment LeftRight { get; }
    }

    // ---- half 2: what the GENERATOR would emit ----------------------------------------------------
    // A token inferred from the member name is upper-cased; a token given in the attribute is verbatim,
    // because the library sends both cases and the distinction is semantic - see the design notes.

    internal static partial class RespLiterals
    {
        internal static partial RespFragment EX => new("$2\r\nEX\r\n"u8);

        internal static partial RespFragment ConfigGet => new("$3\r\nGET\r\n"u8);

        internal static partial RespFragment SetInfoLibName => new("$7\r\nSETINFO\r\n$8\r\nlib-name\r\n"u8, 2);

        internal static partial RespFragment MaxLenApprox => new("$6\r\nMAXLEN\r\n$1\r\n~\r\n"u8, 2);

        internal static partial RespFragment LeftRight => new("$4\r\nLEFT\r\n$5\r\nRIGHT\r\n"u8, 2);
    }

    private static string Frame(in RespFrame frame) => Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    [Fact]
    public void SingleTokenFragment()
    {
        var ctx = new RespContext();
        using var frame = ctx.Execute(RedisCommand.SET, $"{(RedisKey)"k"} {(RedisValue)"v"} {RespLiterals.EX} {(RedisValue)300}");

        Assert.Equal("*5|$3|SET|$1|k|$1|v|$2|EX|$3|300|", Frame(frame));
        Assert.Equal(5, frame.ArgCount);
    }

    [Fact]
    public void TwoTokenFragmentCountsAsTwoArguments()
    {
        // CLIENT SETINFO LIB-NAME StackExchange.Redis
        var ctx = new RespContext();
        using var frame = ctx.Execute(RedisCommand.CLIENT, $"{RespLiterals.SetInfoLibName} {(RedisValue)"StackExchange.Redis"}");

        Assert.Equal("*4|$6|CLIENT|$7|SETINFO|$8|lib-name|$19|StackExchange.Redis|", Frame(frame));

        // the fragment is TWO arguments: *4, not *3 - this is what ArgCount exists for
        Assert.Equal(4, frame.ArgCount);
    }

    [Fact]
    public void MultiTokenFragmentDoesNotShiftKeyMarks()
    {
        // XADD key MAXLEN ~ 1000 * field value - the key precedes a two-token fragment
        var ctx = new RespContext(serverType: ServerType.Cluster);
        var cmd = ctx.Compose(RedisCommand.XADD, $"{(RedisKey)"stream:1"}");
        cmd.AppendFormatted(RespLiterals.MaxLenApprox);
        cmd.AppendFormatted((RedisValue)1000);
        cmd.AppendFormatted((RedisValue)"*");
        using var frame = ctx.Execute(ref cmd);

        Assert.Equal("*6|$4|XADD|$8|stream:1|$6|MAXLEN|$1|~|$4|1000|$1|*|", Frame(frame));
        Assert.Equal(6, frame.ArgCount);

        // the key is still found, and still routes
        Span<KeyRange> ranges = stackalloc KeyRange[2];
        Assert.Equal(1, frame.TryGetKeys(ranges));
        Assert.Equal("stream:1", Encoding.UTF8.GetString(frame.GetKey(ranges[0]).ToArray()));
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"stream:1"), frame.Slot);
    }

    [Fact]
    public void TwoKeysThenATwoTokenFragment()
    {
        // LMOVE source destination LEFT RIGHT - a real command ending in a fixed two-token pair
        var ctx = new RespContext();
        using var frame = ctx.Execute(RedisCommand.LMOVE, $"{(RedisKey)"src"} {(RedisKey)"dst"} {RespLiterals.LeftRight}");

        Assert.Equal("*5|$5|LMOVE|$3|src|$3|dst|$4|LEFT|$5|RIGHT|", Frame(frame));
        Assert.Equal(5, frame.ArgCount);

        Span<KeyRange> ranges = stackalloc KeyRange[2];
        Assert.Equal(2, frame.TryGetKeys(ranges));
        Assert.Equal("src", Encoding.UTF8.GetString(frame.GetKey(ranges[0]).ToArray()));
        Assert.Equal("dst", Encoding.UTF8.GetString(frame.GetKey(ranges[1]).ToArray()));
    }

    [Fact]
    public void KeyAfterAMultiTokenFragmentIsStillTracked()
    {
        // Synthetic: no standard command puts a two-token fragment BETWEEN two keys. The cursor arithmetic
        // has to hold regardless, since ArgCount is what keeps later key-mark positions correct.
        var ctx = new RespContext();
        var cmd = ctx.Compose(RedisCommand.SMOVE, $"{(RedisKey)"src"}");
        cmd.AppendFormatted(RespLiterals.MaxLenApprox);
        cmd.AppendFormatted((RedisKey)"dst");
        using var frame = ctx.Execute(ref cmd);

        Assert.Equal(5, frame.ArgCount);
        Span<KeyRange> ranges = stackalloc KeyRange[2];
        Assert.Equal(2, frame.TryGetKeys(ranges));
        Assert.Equal("src", Encoding.UTF8.GetString(frame.GetKey(ranges[0]).ToArray()));
        Assert.Equal("dst", Encoding.UTF8.GetString(frame.GetKey(ranges[1]).ToArray()));
    }

    [Fact]
    public void ConfigGetReadsAsTheCommandDoes()
    {
        var ctx = new RespContext();
        using var frame = ctx.Execute(RedisCommand.CONFIG, $"{RespLiterals.ConfigGet} {(RedisValue)"maxmemory"}");

        Assert.Equal("*3|$6|CONFIG|$3|GET|$9|maxmemory|", Frame(frame));
    }
}
