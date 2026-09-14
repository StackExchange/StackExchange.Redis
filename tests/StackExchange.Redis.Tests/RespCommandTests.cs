using System;
using System.Collections.Generic;
using System.Text;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <c>RespCommand</c>: a command name resolved once, usable as the command or as an argument naming one.
/// </summary>
public class RespCommandTests
{
    private static string Text(in RespFrame frame) =>
        Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    [Fact]
    public void KnownCommandsStayDeferredSoTheMapStillApplies()
    {
        var get = "GET".Command();
        Assert.True(get.IsKnown);
        Assert.False(get.IsPreformed); // the map holds the bytes, and only the map can

        var renamed = CommandMap.Create(new Dictionary<string, string?> { ["GET"] = "FETCH" });
        using var frame = new RespContext(renamed).Execute($"{get}{(RedisKey)"k"}");
        Assert.Equal("*2|$5|FETCH|$1|k|", Text(frame));
    }

    [Fact]
    public void PreformingAKnownCommandWouldNotBypassTheMap()
    {
        // the flag is about WHEN the encode happens, not about skipping the map; a known command ignores it
        Assert.False("GET".Command(preform: true).IsPreformed);

        var disabled = CommandMap.Create(new HashSet<string> { "GET" }, available: false);
        var ctx = new RespContext(disabled);
        Assert.Throws<RedisCommandException>(() =>
        {
            using var frame = ctx.Execute($"{"GET".Command(preform: true)}{(RedisKey)"k"}");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownCommandsRenderIdenticallyEitherWay(bool preform)
    {
        var search = "FT.SEARCH".Command(preform);
        Assert.False(search.IsKnown);
        Assert.Equal(preform, search.IsPreformed);

        using var frame = new RespContext().Execute($"{search}{(RedisValue)"idx"}");
        Assert.Equal("*2|$9|FT.SEARCH|$3|idx|", Text(frame));
    }

    [Fact]
    public void UnknownCommandsAreUnaffectedByTheCommandMap()
    {
        // CommandMap is built by walking the RedisCommand enum, so an override on a name it cannot parse is
        // silently ignored - which is why preforming a module command is safe
        var renamed = CommandMap.Create(new Dictionary<string, string?> { ["FT.SEARCH"] = "FT.SRCH" });
        using var frame = new RespContext(renamed).Execute($"{"FT.SEARCH".Command()}{(RedisValue)"idx"}");
        Assert.Equal("*2|$9|FT.SEARCH|$3|idx|", Text(frame));
    }

    [Fact]
    public void ACommandCanAlsoBeAnArgumentNamingACommand()
    {
        // COMMAND INFO <name>: the server knows a renamed command ONLY by its new name, so the argument has
        // to be the mapped spelling - taking it from the map is the only way to get that right
        var renamed = CommandMap.Create(new Dictionary<string, string?> { ["HGET"] = "HASHGET" });
        var ctx = new RespContext(renamed);

        using var frame = ctx.Execute($"{"COMMAND".Command()}{RespLiterals.Info}{"HGET".Command()}");
        Assert.Equal("*3|$7|COMMAND|$4|INFO|$7|HASHGET|", Text(frame));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("GET KEY")]
    [InlineData("GET\r\nEVIL")]
    [InlineData("GET\n")]
    public void MalformedNamesAreRejectedAtResolutionNotOnTheWire(string name)
    {
        // a CR or LF reaching the stream desynchronises every subsequent command; this is checked once,
        // where it costs nothing, rather than risked per call
        Assert.ThrowsAny<ArgumentException>(() => name.Command());
    }

    [Fact]
    public void Utf8LiteralsNeedNoStringAtAll()
    {
        var fromString = "FT.SEARCH".Command(preform: true);
        var fromBytes = "FT.SEARCH"u8.Command();

        Assert.False(fromBytes.IsKnown);
        using var a = new RespContext().Execute($"{fromString}{(RedisValue)"idx"}");
        using var b = new RespContext().Execute($"{fromBytes}{(RedisValue)"idx"}");
        Assert.Equal(Text(a), Text(b));
    }

    [Fact]
    public void Utf8LiteralsAlsoResolveKnownCommandsThroughTheMap()
    {
        // TryParseCI matches on bytes directly, so a u8 literal needs no transcoding even when known
        Assert.True("GET"u8.Command().IsKnown);

        var renamed = CommandMap.Create(new Dictionary<string, string?> { ["GET"] = "FETCH" });
        using var frame = new RespContext(renamed).Execute($"{"GET"u8.Command()}{(RedisKey)"k"}");
        Assert.Equal("*2|$5|FETCH|$1|k|", Text(frame));
    }

    internal static partial class RespLiterals
    {
#pragma warning disable SER011 // stands in for the generator
        internal static RespFragment Info => new("$4\r\nINFO\r\n"u8);
#pragma warning restore SER011
    }
}
