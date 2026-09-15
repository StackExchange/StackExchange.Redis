using System;
using System.Collections.Generic;
using System.Text;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Shows how fixed RESP tokens would be authored: a declared partial property, and the body a generator
/// would emit. Both halves are hand-written here - the point is the usage, not the generator.
/// See design/interpolated-resp-writer.md section 2.3.
/// </summary>
public partial class InterpolatedWriterFragmentTests
{
    // The author writes the declaration; RespFragmentGenerator emits the body. The attribute is only
    // needed when the token differs from the member name, or when the fragment spans more than one token.

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

    // half 2 - the bodies - is emitted by RespFragmentGenerator from the declarations above.

    private static string Frame(in RespFrame frame) => Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    [Fact]
    public void SingleTokenFragment()
    {
        var ctx = new RespContext();
        using var frame = ctx.Render(RedisCommand.SET, $"{(RedisKey)"k"} {(RedisValue)"v"} {RespLiterals.EX} {(RedisValue)300}");

        Assert.Equal("*5|$3|SET|$1|k|$1|v|$2|EX|$3|300|", Frame(frame));
        Assert.Equal(5, frame.ArgCount);
    }

    [Fact]
    public void TwoTokenFragmentCountsAsTwoArguments()
    {
        // CLIENT SETINFO LIB-NAME StackExchange.Redis
        var ctx = new RespContext();
        using var frame = ctx.Render(RedisCommand.CLIENT, $"{RespLiterals.SetInfoLibName} {(RedisValue)"StackExchange.Redis"}");

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
        using var frame = ctx.Render(ref cmd);

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
        using var frame = ctx.Render(RedisCommand.LMOVE, $"{(RedisKey)"src"} {(RedisKey)"dst"} {RespLiterals.LeftRight}");

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
        using var frame = ctx.Render(ref cmd);

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
        using var frame = ctx.Render(RedisCommand.CONFIG, $"{RespLiterals.ConfigGet} {(RedisValue)"maxmemory"}");

        Assert.Equal("*3|$6|CONFIG|$3|GET|$9|maxmemory|", Frame(frame));
    }

    // ---- the public, string-based command overload -------------------------------------------------

    [Fact]
    public void StringCommandIsSpeculativelyParsedAndAliased()
    {
        // a recognised name goes through the command map, so renames still apply
        var map = CommandMap.Create(new Dictionary<string, string?> { ["get"] = "xget" });
        var ctx = new RespContext(map);
        using var frame = ctx.Render("get", $"{(RedisKey)"k"}");

        Assert.Equal("*2|$4|XGET|$1|k|", Frame(frame));
    }

    [Fact]
    public void StringCommandIsCaseInsensitive()
    {
        var ctx = new RespContext();
        using var upper = ctx.Render("GET", $"{(RedisKey)"k"}");
        using var lower = ctx.Render("get", $"{(RedisKey)"k"}");

        Assert.Equal("*2|$3|GET|$1|k|", Frame(upper));
        Assert.True(upper.Span.SequenceEqual(lower.Span));
    }

    [Fact]
    public void StringCommandRespectsDisabledCommands()
    {
        var map = CommandMap.Create(new Dictionary<string, string?> { ["get"] = null });
        var ctx = new RespContext(map);

        Assert.Throws<RedisCommandException>(() => ctx.Render("get", $"{(RedisKey)"k"}").Dispose());
    }

    [Fact]
    public void UnrecognisedStringCommandIsFramedVerbatim()
    {
        // not a known command: no aliasing to apply, so the name goes out as written - matching
        // IDatabase.Execute(string, ...) behaviour for ad-hoc commands
        var ctx = new RespContext();
        using var frame = ctx.Render("FT.SEARCH", $"{(RedisValue)"idx"}{(RedisValue)"*"}");

        Assert.Equal("*3|$9|FT.SEARCH|$3|idx|$1|*|", Frame(frame));
    }

    // ---- the sanctioned runtime route --------------------------------------------------------------

    [Fact]
    public void CreateValidatedAcceptsWellFormedBytes()
    {
        var fragment = RespFragment.CreateValidated("$2\r\nEX\r\n"u8);
        Assert.Equal(1, fragment.ArgCount);

        var two = RespFragment.CreateValidated("$6\r\nMAXLEN\r\n$1\r\n~\r\n"u8, 2);
        Assert.Equal(2, two.ArgCount);
    }

    [Theory]
    // the failure modes that would otherwise desync the connection, each caught here instead
    [InlineData("2\r\nEX\r\n", 1)]                 // no '$'
    [InlineData("$\r\nEX\r\n", 1)]                 // no length
    [InlineData("$2EX\r\n", 1)]                      // no CRLF after the length
    [InlineData("$3\r\nEX\r\n", 1)]                // length disagrees with the payload
    [InlineData("$2\r\nEX", 1)]                      // truncated
    [InlineData("$2\r\nEXXX", 1)]                    // no CRLF after the payload
    [InlineData("$2\r\nEX\r\n$2\r\nNX\r\n", 1)] // two fragments declared as one
    [InlineData("$2\r\nEX\r\n", 2)]                // one fragment declared as two
    public void CreateValidatedRejectsMalformedBytes(string raw, int argCount)
    {
        var bytes = Encoding.UTF8.GetBytes(raw);
        var error = Assert.Throws<ArgumentException>(() => RespFragment.CreateValidated(bytes, argCount));
        Assert.Contains("well-formed", error.Message);
    }

    [Fact]
    public void ValidatedFragmentsWriteLikeGeneratedOnes()
    {
        var ctx = new RespContext();
        using var generated = ctx.Render(RedisCommand.SET, $"{(RedisKey)"k"} {(RedisValue)"v"} {RespLiterals.EX}");
        using var validated = ctx.Render(RedisCommand.SET, $"{(RedisKey)"k"} {(RedisValue)"v"} {RespFragment.CreateValidated("$2\r\nEX\r\n"u8)}");

        Assert.True(generated.Span.SequenceEqual(validated.Span));
    }
}
