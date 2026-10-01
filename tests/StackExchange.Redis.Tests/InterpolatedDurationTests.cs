using System;
using System.Text;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Durations in a command hole: <c>$"{ttl:s}"</c>, <c>$"{idle:ms}"</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unit is ours, not <see cref="TimeSpan"/>'s</b>, so an unrecognised one throws rather than
/// falling back to anything. A duration written in the wrong unit is still a <i>valid</i> command - the
/// server cannot object to <c>EXPIRE k 5000</c> when you meant five seconds - so this is one of the few
/// places where the only possible check is at the call site.
/// </para>
/// <para>
/// The overload exists because <see cref="TimeSpan"/> is a BCL type and so can never implement
/// <c>IRespArgument</c>. That turns out to be the useful half: a unit-less <c>$"{ttl}"</c> does not
/// compile at all, which is a better outcome than any runtime rule.
/// </para>
/// </remarks>
public class InterpolatedDurationTests
{
    private static readonly RespContext Ctx = new();

    private static string Text(in RespRequestFrame frame) =>
        Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    [Theory]
    [InlineData("s", 5, "*3|$6|EXPIRE|$1|k|$1|5|")]
    [InlineData("ms", 5, "*3|$6|EXPIRE|$1|k|$4|5000|")]
    public void TheUnitDecidesWhatIsWritten(string unit, int seconds, string expected)
    {
        var ttl = TimeSpan.FromSeconds(seconds);
        var cmd = unit == "s"
            ? Ctx.Compose($"{RedisCommand.EXPIRE}{(RedisKey)"k"}{ttl:s}")
            : Ctx.Compose($"{RedisCommand.EXPIRE}{(RedisKey)"k"}{ttl:ms}");
        using var frame = Ctx.Render(ref cmd);
        Assert.Equal(expected, Text(frame));
    }

    /// <summary>Whole units, truncated - which is what the wire can carry.</summary>
    /// <remarks>
    /// This is the bug that prompted the overload: <c>XREADGROUP</c> passed
    /// <c>TimeSpan.TotalMilliseconds</c> - a <see cref="double"/> - straight to the writer, so
    /// <c>TimeSpan.FromMilliseconds(1500.5)</c> put <c>CLAIM 1500.5</c> on the wire and the server
    /// answered "value is not an integer".
    /// </remarks>
    [Fact]
    public void FractionsAreTruncatedRatherThanWritten()
    {
        var idle = TimeSpan.FromTicks((TimeSpan.TicksPerMillisecond * 1500) + 5000);
        var cmd = Ctx.Compose($"{RedisCommand.PEXPIRE}{(RedisKey)"k"}{idle:ms}");
        using var frame = Ctx.Render(ref cmd);
        Assert.Equal("*3|$7|PEXPIRE|$1|k|$4|1500|", Text(frame));
    }

    [Theory]
    [InlineData("")]
    [InlineData("S")]
    [InlineData("sec")]
    [InlineData("millis")]
    [InlineData("hh:mm:ss")]
    public void AnUnknownUnitThrows(string unit)
    {
        var ttl = TimeSpan.FromSeconds(5);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var handler = new RespRequestBuilder(0, 1, Ctx, RedisCommand.EXPIRE);
            try
            {
                handler.AppendFormatted(ttl, unit);
            }
            finally
            {
                handler.Dispose();
            }
        });
        Assert.Equal("format", ex.ParamName);
    }

    /// <summary>A null duration writes nothing at all, so it pairs with a conditional token.</summary>
    [Fact]
    public void ANullDurationWritesNothing()
    {
        TimeSpan? absent = null, present = TimeSpan.FromSeconds(3);

        var without = Ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}{RespLiterals.Ex.When(absent)}{absent:s}");
        using var a = Ctx.Render(ref without);
        Assert.Equal("*2|$3|SET|$1|k|", Text(a));

        var with = Ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}{RespLiterals.Ex.When(present)}{present:s}");
        using var b = Ctx.Render(ref with);
        Assert.Equal("*4|$3|SET|$1|k|$2|EX|$1|3|", Text(b));
    }
}
