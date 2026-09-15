using System;
using System.Linq;
using System.Reflection;
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

    [Fact]
    public void AnAppendAcceptsExactlyWhatTheCommandDoes()
    {
        // by construction, not by keeping two lists aligned: the handler for an append IS the command
        // handler, so there is one set of overloads and nothing to fall out of step. An earlier design
        // used a separate proxy type and needed a test to guard exactly this.
        var accepted = typeof(RespCommandHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "AppendFormatted")
            .Select(m => m.GetParameters()[0].ParameterType.Name) // [0] is the value; a format may follow
            .ToArray();

        Assert.Contains("RedisKey", accepted);
        Assert.Contains("RedisValue", accepted);
        Assert.Contains("RespFragment", accepted);
    }

    [Theory]
    [InlineData(false, "*3|$3|SET|$1|k|$1|v|")]
    [InlineData(true, "*5|$3|SET|$1|k|$1|v|$2|EX|$3|300|")]
    public void ConditionalAppendMatchesTheUnconditionalForm(bool withTtl, string expected)
    {
        var cmd = Ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");
        if (withTtl) cmd.Append($"{RespLiterals.EX}{(RedisValue)300}");

        using var frame = Ctx.Render(ref cmd);
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

        using var frame = Ctx.Render(ref cmd);
        var text = Text(frame);
        Assert.StartsWith("*4|$3|SET|$1|k|$4096|", text);
        Assert.Equal(4, frame.ArgCount);
    }

    [Fact]
    public void TheSourceIsEmptiedForTheDurationOfTheAppend()
    {
        // the move is completed at both ends: the constructor resets the source, so the command is not a
        // second owner of the pooled array while the fragment is being written. Spelled out here the way
        // the compiler spells it, because that window is not observable from `cmd.Append($"...")`.
        var cmd = Ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}");

        var handler = new RespCommandHandler(0, 1, ref cmd);

        // cmd now owns nothing: every path off it is a clean throw or a no-op, never a double-free.
        // Spelled as try/catch rather than Assert.Throws because a ref struct cannot be captured by a lambda.
        Assert.True(CompleteThrows(ref cmd), "the moved-from command should be empty");
        cmd.Dispose(); // no-op; would be a second return to the pool if the reset had not happened

        handler.AppendFormatted((RedisValue)"v");
        RespAppend.Append(ref cmd, ref handler);

        // ...and the other end: the handler has been emptied in turn
        Assert.True(CompleteThrows(ref handler), "the moved-from handler should be empty");

        using var frame = Ctx.Render(ref cmd);
        Assert.Equal("*3|$3|SET|$1|k|$1|v|", Text(frame));
    }

    /// <summary>Whether <c>Complete</c> rejects this handler as empty, without disturbing it if it does.</summary>
    private static bool CompleteThrows(ref RespCommandHandler handler)
    {
        try
        {
            handler.Complete().Dispose();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    [Fact]
    public void SeveralAppendsAccumulate()
    {
        var cmd = Ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");
        cmd.Append($"{RespLiterals.EX}{(RedisValue)300}");
        cmd.Append($"{(RedisValue)"XX"}");

        using var frame = Ctx.Render(ref cmd);
        Assert.Equal("*6|$3|SET|$1|k|$1|v|$2|EX|$3|300|$2|XX|", Text(frame));
    }

    [Fact]
    public void KeysAppendedThisWayAreStillMarked()
    {
        var cmd = Ctx.Compose($"{RedisCommand.MGET}{(RedisKey)"a"}");
        cmd.Append($"{(RedisKey)"b"}");

        using var frame = Ctx.Render(ref cmd);
        Assert.Equal(2, frame.KeyCount);
        var ranges = new KeyRange[2];
        Assert.Equal(2, frame.TryGetKeys(ranges));
        Assert.Equal("a", Encoding.UTF8.GetString(frame.GetKey(ranges[0]).ToArray()));
        Assert.Equal("b", Encoding.UTF8.GetString(frame.GetKey(ranges[1]).ToArray()));
    }
    /// <summary>
    /// A frame past the protocol's argument limit is refused when it is closed, not when it is sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Complete</c> is where the count is final and where the <c>*N</c> header is about to be written, so
    /// a frame past the limit is invalid by construction from that point on. Waiting for dispatch would also
    /// miss the frames that are never dispatched at all - a cache lookup key, an ad-hoc composition, or
    /// anything built through the public <c>Render</c>.
    /// </para>
    /// <para>
    /// The limit is counted the way the writer counts it - arguments <i>without</i> the command - so a frame
    /// and the equivalent classic message are accepted and refused at exactly the same point. Spelled with
    /// try/catch rather than <c>Assert.Throws</c> because a ref struct cannot be captured by a lambda.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFramePastTheArgumentLimitIsRefusedWhenClosed()
    {
        const int Max = 1024 * 1024; // MessageWriter.REDIS_MAX_ARGS

        // command + (Max - 1) arguments: the largest frame that is still legal
        var cmd = Ctx.Compose($"{RedisCommand.RPUSH}{(RedisKey)"k"}");
        for (var i = 2; i < Max; i++) cmd.Append($"{(RedisValue)1}");
        using (var ok = Ctx.Render(ref cmd))
        {
            Assert.Equal(Max, ok.ArgCount); // the frame's own count includes the command
        }

        // one more, and it is refused - without leaking the several megabytes it had rented
        var over = Ctx.Compose($"{RedisCommand.RPUSH}{(RedisKey)"k"}");
        for (var i = 2; i <= Max; i++) over.Append($"{(RedisValue)1}");
        Assert.True(RenderThrows(ref over), "a frame past the limit should be refused when closed");
        over.Dispose(); // no-op if the refusal returned the buffer; a double-return otherwise
    }

    /// <summary>Whether closing this command is refused for being over the argument limit.</summary>
    private static bool RenderThrows(ref RespCommandHandler handler)
    {
        try
        {
            handler.Complete().Dispose();
            return false;
        }
        catch (RedisCommandException)
        {
            return true;
        }
    }

    /// <summary>A refused close hands its buffer back rather than leaking it.</summary>
    /// <remarks>
    /// The buffer is rented by the time <c>Complete</c> runs - the handler is built in the caller's frame,
    /// before the method is entered - so throwing without returning it would leak a pooled array, and for
    /// the over-limit case a very large one.
    /// </remarks>
    [Fact]
    public void ARefusedCloseReturnsTheBuffer()
    {
        var empty = new RespCommandHandler(0, 0, Ctx);
        Assert.True(CompleteThrows(ref empty), "an empty command should be refused");
        empty.Dispose(); // no-op if Complete gave the buffer back; a double-return otherwise
    }
}
