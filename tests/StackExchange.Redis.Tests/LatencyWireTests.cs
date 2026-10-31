using System.Text;
using StackExchange.Redis.Protocol;
using Xunit;
using LibRespLiterals = StackExchange.Redis.RespLiterals; // the test suite has its own RespLiterals partial

namespace StackExchange.Redis.Tests;

/// <summary>
/// What <c>LATENCY RESET</c> puts on the wire when the caller names events - the operand writes the
/// <see cref="string"/> names as they are, with no intermediate array.
/// </summary>
public class LatencyWireTests
{
    [Fact]
    public void NamesWriteAsTheyAre()
        => Assert.Equal(
            "*4|$7|LATENCY|$5|RESET|$7|command|$9|fork-wait|",
            Render(new(["command", "fork-wait"])));

    private static string Render(Diagnostics.EventNamesOperand operand)
    {
        var ctx = new RespContext();
        using var frame = ctx.Render($"{RedisCommand.LATENCY}{LibRespLiterals.Reset}{operand}");
        return Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");
    }
}
