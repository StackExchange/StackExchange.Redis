using System.Text;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A bare <see cref="string"/> in a command hole: what it binds to, and what it is <i>not</i>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It used not to compile.</b> <see cref="RedisKey"/>, <see cref="RedisValue"/> and
/// <see cref="RedisChannel"/> all convert implicitly from <see cref="string"/> and none is better than the
/// others, so <c>$"{cmd}{s}"</c> was CS0121 - naming two of the three arbitrarily, which tells a caller
/// nothing about the actual choice in front of them. <c>AppendFormatted(string?)</c> is an exact match and
/// a standard conversion beats every user-defined one, so it wins outright; no
/// <c>OverloadResolutionPriority</c> is involved.
/// </para>
/// <para>
/// <b>It binds to the value overload, and that is silent</b> - which is the whole reason these tests are
/// written down. An unmarked argument gets no key prefix, no routing slot and no cache invalidation, so if
/// the resolution ever drifted to something else, or someone "helpfully" marked strings as keys, the
/// damage would be invisible at the call site.
/// </para>
/// </remarks>
public class InterpolatedStringHoleTests
{
    private static readonly RespContext Ctx = new();

    private static string Text(in RespRequestFrame frame) =>
        Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    [Fact]
    public void ABareStringRendersAsAValue()
    {
        var s = "v";
        var cmd = Ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}{s}");
        using var frame = Ctx.Render(ref cmd);

        Assert.Equal("*3|$3|SET|$1|k|$1|v|", Text(frame));
    }

    [Fact]
    public void ABareStringIsNotMarkedAsAKey()
    {
        // the cast is the only thing that makes a string a key, and it has to stay that way: routing,
        // prefixing and invalidation all read this count
        var s = "k2";
        var cmd = Ctx.Compose($"{RedisCommand.MGET}{(RedisKey)"k1"}{s}");
        using var frame = Ctx.Render(ref cmd);

        Assert.Equal(1, frame.KeyCount);
    }

    [Fact]
    public void ItAgreesWithTheExplicitValueCast()
    {
        // the overload is a disambiguation, not a second behaviour
        var s = "hello";
        var implicitly_ = Ctx.Compose($"{RedisCommand.ECHO}{s}");
        var explicitly = Ctx.Compose($"{RedisCommand.ECHO}{(RedisValue)s}");

        using var a = Ctx.Render(ref implicitly_);
        using var b = Ctx.Render(ref explicitly);
        Assert.Equal(Text(b), Text(a));
    }

    [Fact]
    public void ANullStringWritesAnEmptyArgument()
    {
        // RedisValue.Null's rendering, reached by the same route - null is a value here, not an absent
        // argument; that distinction belongs to the long?/RespFragment.When pair
        string? s = null;
        var cmd = Ctx.Compose($"{RedisCommand.ECHO}{s}");
        using var frame = Ctx.Render(ref cmd);

        Assert.Equal("*2|$4|ECHO|$0||", Text(frame));
        Assert.Equal(2, frame.ArgCount);
    }
}
