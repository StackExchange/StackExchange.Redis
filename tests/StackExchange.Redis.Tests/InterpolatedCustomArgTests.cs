using System;
using System.Text;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="IRespArgument"/>: the one way a type from another assembly can appear in a command hole.
/// </summary>
/// <remarks>
/// Every assertion here is an overload-resolution fact that was measured rather than reasoned about,
/// because the design notes (2.2) previously ruled out <c>AppendFormatted&lt;T&gt;</c> outright and the
/// constrained form is only an exception to that if these come out the way they do.
/// </remarks>
public class InterpolatedCustomArgTests
{
    private static readonly RespContext Ctx = new();

    private static string Text(in RespFrame frame) =>
        Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    /// <summary>A struct, so the constrained call has something to box if it is going to.</summary>
    private readonly struct Window(int from, int to) : IRespArgument
    {
        public void WriteTo(scoped ref RespCommandHandler handler)
        {
            handler.AppendFormatted((RedisValue)from);
            handler.AppendFormatted((RedisValue)to);
        }
    }

    /// <summary>Writes nothing: an absent optional argument is no argument, not an empty one.</summary>
    private readonly struct Absent : IRespArgument
    {
        public void WriteTo(scoped ref RespCommandHandler handler) { }
    }

    /// <summary>Opts in AND converts to <see cref="RedisValue"/>, so both overloads are applicable.</summary>
    private readonly struct Ambiguous : IRespArgument
    {
        public static implicit operator RedisValue(Ambiguous value) => "CONVERSION";
        public void WriteTo(scoped ref RespCommandHandler handler) => handler.AppendFormatted((RedisValue)"INTERFACE");
    }

    [Fact]
    public void ACustomTypeCanAppearInAHole()
    {
        using var frame = Ctx.Execute($"{RedisCommand.ZRANGE}{(RedisKey)"k"}{new Window(0, 9)}");
        Assert.Equal("*4|$6|ZRANGE|$1|k|$1|0|$1|9|", Text(frame));
        Assert.Equal(4, frame.ArgCount);
    }

    [Fact]
    public void WritingNothingContributesNoArgument()
    {
        using var frame = Ctx.Execute($"{RedisCommand.GET}{(RedisKey)"k"}{new Absent()}");
        Assert.Equal("*2|$3|GET|$1|k|", Text(frame));
        Assert.Equal(2, frame.ArgCount);
    }

    [Fact]
    public void AStructImplementerDoesNotBox()
    {
        // a constrained call on a value type, so the interface dispatch costs no allocation; if this
        // regresses to a boxing call it is one allocation per argument per command, on the hot path
        static long Measure()
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 64; i++)
            {
                using var frame = Ctx.Execute($"{RedisCommand.ZRANGE}{(RedisKey)"k"}{new Window(0, 9)}");
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Measure(); // discard the first pass: buffer rental warms the pool
        Assert.Equal(0, Measure());
    }

    [Fact]
    public void OptingInBeatsAnIncidentalConversion()
    {
        // both AppendFormatted(RedisValue) (via the implicit operator) and AppendFormatted<T> apply; the
        // generic is an exact match by inference and wins. That is the WANTED answer here - implementing
        // the interface is a deliberate statement about how the type should be written - but it is the
        // same mechanism the design notes warn about for an unconstrained generic, so it is pinned.
        using var frame = Ctx.Execute($"{RedisCommand.GET}{(RedisKey)"k"}{new Ambiguous()}");
        Assert.Equal("*3|$3|GET|$1|k|$9|INTERFACE|", Text(frame));
    }

    [Fact]
    public void AKeyAfterACustomArgumentIsStillMarkedCorrectly()
    {
        // the implementer writes through the handler's own counters, so it cannot misreport how many
        // arguments it wrote - which is what would otherwise shift every key mark after it
        using var frame = Ctx.Execute($"{RedisCommand.MGET}{(RedisKey)"a"}{new Window(0, 9)}{(RedisKey)"b"}");

        Assert.Equal(5, frame.ArgCount);
        Assert.Equal(2, frame.KeyCount);
        var ranges = new KeyRange[2];
        Assert.Equal(2, frame.TryGetKeys(ranges));
        Assert.Equal("a", Encoding.UTF8.GetString(frame.GetKey(ranges[0]).ToArray()));
        Assert.Equal("b", Encoding.UTF8.GetString(frame.GetKey(ranges[1]).ToArray()));
    }
}
