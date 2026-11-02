using System.Text;
using StackExchange.Redis.Protocol;
using Xunit;
using LibRespLiterals = StackExchange.Redis.RespLiterals; // the test suite has its own RespLiterals partial

namespace StackExchange.Redis.Tests;

/// <summary>
/// What <c>CLIENT TRACKING ON BCAST</c> puts on the wire, byte for byte - the operand carries the
/// <c>PREFIX p</c> pairs, so the shape is pinned here rather than discovered against a live server.
/// </summary>
public class ClientTrackingWireTests
{
    [Fact]
    public void BcastAloneWhenNoPrefixes()
        => Assert.Equal(
            "*4|$6|CLIENT|$8|TRACKING|$2|ON|$5|BCAST|",
            Render(new([])));

    [Fact]
    public void EachPrefixBehindItsOwnKeyword()
        => Assert.Equal(
            "*8|$6|CLIENT|$8|TRACKING|$2|ON|$5|BCAST|$6|PREFIX|$4|usr:|$6|PREFIX|$5|sess:|",
            Render(new(["usr:"u8.ToArray(), "sess:"u8.ToArray()])));

    private static string Render(RespHandshake.TrackingPrefixesOperand operand)
    {
        var ctx = new RespContext();
        using var frame = ctx.Render($"{RedisCommand.CLIENT}{LibRespLiterals.Tracking}{LibRespLiterals.On}{LibRespLiterals.Bcast}{operand}");
        return Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");
    }
}
