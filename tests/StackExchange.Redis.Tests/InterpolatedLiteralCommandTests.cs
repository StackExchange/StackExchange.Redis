using System;
using System.Collections.Generic;
using System.Text;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Literal text as command and arguments: <c>$"SET {key} {value}"</c>. Suboptimal but correct - see the
/// design notes, section 2.1.
/// </summary>
public class InterpolatedLiteralCommandTests
{
    // these tests exist to exercise the form SER309 warns about, so the warning is suppressed here and
    // nowhere wider - the same discipline the generator uses for SER011
#pragma warning disable SER309

    private static string Text(in RespFrame frame) =>
        Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    [Fact]
    public void ALeadingLiteralIsTheCommand()
    {
        var ctx = new RespContext();
        using var literal = ctx.Execute($"SET {(RedisKey)"mykey"} {(RedisValue)"marc"}");
        using var holes = ctx.Execute($"{RedisCommand.SET}{(RedisKey)"mykey"}{(RedisValue)"marc"}");

        // the whole point: the readable spelling must produce the identical frame, or it is not an
        // alternative spelling, it is a second implementation
        Assert.Equal("*3|$3|SET|$5|mykey|$4|marc|", Text(literal));
        Assert.Equal(Text(holes), Text(literal));
    }

    [Fact]
    public void TheLeadingCommandStillGoesThroughTheCommandMap()
    {
        var renamed = CommandMap.Create(new Dictionary<string, string?> { ["SET"] = "STORE" });
        using var frame = new RespContext(renamed).Execute($"SET {(RedisKey)"k"} {(RedisValue)"v"}");
        Assert.Equal("*3|$5|STORE|$1|k|$1|v|", Text(frame));
    }

    [Fact]
    public void ADisabledLeadingCommandThrows()
    {
        var disabled = CommandMap.Create(new HashSet<string> { "SET" }, available: false);
        var ctx = new RespContext(disabled);
        Assert.Throws<RedisCommandException>(() =>
        {
            using var frame = ctx.Execute($"SET {(RedisKey)"k"} {(RedisValue)"v"}");
        });
    }

    [Fact]
    public void SplittingOnWhitespaceGetsContainerCommandsRight()
    {
        // CONFIG is the command and IS mapped; GET is an ordinary argument and is NOT - which is exactly
        // how CommandMap works, since it maps container verbs only
        using var frame = new RespContext().Execute($"CONFIG GET {(RedisValue)"maxmemory"}");
        Assert.Equal("*3|$6|CONFIG|$3|GET|$9|maxmemory|", Text(frame));
        Assert.Equal(3, frame.ArgCount);
    }

    [Fact]
    public void LiteralsAfterTheCommandAreOrdinaryArguments()
    {
        using var frame = new RespContext().Execute(
            $"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"} EX {(RedisValue)300}");
        Assert.Equal("*5|$3|SET|$1|k|$1|v|$2|EX|$3|300|", Text(frame));
    }

    [Fact]
    public void WhitespaceOnlyLiteralsStillContributeNothing()
    {
        var ctx = new RespContext();
        using var spaced = ctx.Execute($"{RedisCommand.GET} {(RedisKey)"k"}");
        using var tight = ctx.Execute($"{RedisCommand.GET}{(RedisKey)"k"}");
        Assert.Equal(Text(tight), Text(spaced));
        Assert.Equal(2, spaced.ArgCount);
    }

    [Fact]
    public void RunsOfWhitespaceCollapse()
    {
        using var frame = new RespContext().Execute($"CONFIG   GET    {(RedisValue)"maxmemory"}");
        Assert.Equal("*3|$6|CONFIG|$3|GET|$9|maxmemory|", Text(frame));
    }

    [Fact]
    public void AnUnknownLeadingCommandIsFramedVerbatim()
    {
        using var frame = new RespContext().Execute($"FT.SEARCH {(RedisValue)"idx"}");
        Assert.Equal("*2|$9|FT.SEARCH|$3|idx|", Text(frame));
    }

    [Fact]
    public void TheCommandInfoShapeWorks()
    {
        // the motivating example: a command name as an argument, alongside a literal subcommand
        var renamed = CommandMap.Create(new Dictionary<string, string?> { ["HGET"] = "HASHGET" });
        using var frame = new RespContext(renamed).Execute($"COMMAND INFO {"HGET".Command()}");

        // the argument must be the MAPPED name - the server knows a renamed command only by that
        Assert.Equal("*3|$7|COMMAND|$4|INFO|$7|HASHGET|", Text(frame));
    }

    [Fact]
    public void NonAsciiLiteralsEncodeCorrectly()
    {
        using var frame = new RespContext().Execute($"ECHO héllo{(RedisValue)"!"}");
        Assert.Equal("*3|$4|ECHO|$6|héllo|$1|!|", Text(frame));
    }

    [Fact]
    public void KeysAreStillOnlyMarkedFromKeyHoles()
    {
        using var frame = new RespContext().Execute($"SET {(RedisKey)"k"} {(RedisValue)"v"}");

        // a literal token is never a key: it cannot be, since key-ness is what the hole type says
        Assert.Equal(1, frame.KeyCount);
        var ranges = new KeyRange[1];
        Assert.Equal(1, frame.TryGetKeys(ranges));
        Assert.Equal("k", Encoding.UTF8.GetString(frame.GetKey(ranges[0]).ToArray()));
    }
#pragma warning restore SER309
}
