using System;
using System.Reflection;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class Streams(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    // the multi-stream XREAD/XREADGROUP handler, as the command sends it
    private static readonly IRespHandler<RedisStream[]> Handler
        = GroupHandlers.Get<RedisStream[]>(typeof(global::StackExchange.Redis.Streams), "StreamTypesHandler", "NamedStreams");

    // Multi-stream read tests - XREAD command
    // Format: array of [stream_name, array of entries]
    // Each entry is [id, array of name/value pairs]
    [Fact]
    public void MultiStreamProcessor_EmptyResult()
    {
        // Server returns nil when no data available
        var resp = "*-1\r\n";
        var result = Execute(resp, Handler);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void MultiStreamProcessor_EmptyArray()
    {
        // Server returns empty array
        var resp = "*0\r\n";
        var result = Execute(resp, Handler);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void MultiStreamProcessor_SingleStreamSingleEntry()
    {
        // XREAD COUNT 1 STREAMS mystream 0-0
        // 1) 1) "mystream"
        //    2) 1) 1) "1526984818136-0"
        //          2) 1) "duration"
        //             2) "1532"
        //             3) "event-id"
        //             4) "5"
        var resp = "*1\r\n" + // 1 stream
                   "*2\r\n" + // [stream_name, entries]
                   "$8\r\nmystream\r\n" + // stream name
                   "*1\r\n" + // 1 entry
                   "*2\r\n" + // [id, values]
                   "$15\r\n1526984818136-0\r\n" + // entry id
                   "*4\r\n" + // 2 name/value pairs (interleaved)
                   "$8\r\nduration\r\n" +
                   "$4\r\n1532\r\n" +
                   "$8\r\nevent-id\r\n" +
                   "$1\r\n5\r\n";

        var result = Execute(resp, Handler);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("mystream", (string?)result[0].Key);
        Assert.Single(result[0].Entries);
        Assert.Equal("1526984818136-0", (string?)result[0].Entries[0].Id);
        Assert.Equal(2, result[0].Entries[0].Values.Length);
        Assert.Equal("duration", (string?)result[0].Entries[0].Values[0].Name);
        Assert.Equal("1532", (string?)result[0].Entries[0].Values[0].Value);
        Assert.Equal("event-id", (string?)result[0].Entries[0].Values[1].Name);
        Assert.Equal("5", (string?)result[0].Entries[0].Values[1].Value);
    }

    [Fact]
    public void MultiStreamProcessor_MultipleStreamsMultipleEntries()
    {
        // XREAD COUNT 2 STREAMS mystream writers 0-0 0-0
        // (see RespParsers.ParseRedisStreams for the redis-cli format)
        var resp = "*2\r\n" + // 2 streams
                   // First stream: mystream
                   "*2\r\n" +
                   "$8\r\nmystream\r\n" +
                   "*2\r\n" + // 2 entries
                   "*2\r\n" +
                   "$15\r\n1526984818136-0\r\n" +
                   "*4\r\n" +
                   "$8\r\nduration\r\n$4\r\n1532\r\n$8\r\nevent-id\r\n$1\r\n5\r\n" +
                   "*2\r\n" +
                   "$15\r\n1526999352406-0\r\n" +
                   "*4\r\n" +
                   "$8\r\nduration\r\n$3\r\n812\r\n$8\r\nevent-id\r\n$1\r\n9\r\n" +
                   // Second stream: writers
                   "*2\r\n" +
                   "$7\r\nwriters\r\n" +
                   "*2\r\n" + // 2 entries
                   "*2\r\n" +
                   "$15\r\n1526985676425-0\r\n" +
                   "*4\r\n" +
                   "$4\r\nname\r\n$8\r\nVirginia\r\n$7\r\nsurname\r\n$5\r\nWoolf\r\n" +
                   "*2\r\n" +
                   "$15\r\n1526985685298-0\r\n" +
                   "*4\r\n" +
                   "$4\r\nname\r\n$4\r\nJane\r\n$7\r\nsurname\r\n$6\r\nAusten\r\n";

        var result = Execute(resp, Handler);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);

        // First stream: mystream
        Assert.Equal("mystream", (string?)result[0].Key);
        Assert.Equal(2, result[0].Entries.Length);
        Assert.Equal("1526984818136-0", (string?)result[0].Entries[0].Id);
        Assert.Equal("duration", (string?)result[0].Entries[0].Values[0].Name);
        Assert.Equal("1532", (string?)result[0].Entries[0].Values[0].Value);
        Assert.Equal("event-id", (string?)result[0].Entries[0].Values[1].Name);
        Assert.Equal("5", (string?)result[0].Entries[0].Values[1].Value);
        Assert.Equal("1526999352406-0", (string?)result[0].Entries[1].Id);
        Assert.Equal("duration", (string?)result[0].Entries[1].Values[0].Name);
        Assert.Equal("812", (string?)result[0].Entries[1].Values[0].Value);

        // Second stream: writers
        Assert.Equal("writers", (string?)result[1].Key);
        Assert.Equal(2, result[1].Entries.Length);
        Assert.Equal("1526985676425-0", (string?)result[1].Entries[0].Id);
        Assert.Equal("name", (string?)result[1].Entries[0].Values[0].Name);
        Assert.Equal("Virginia", (string?)result[1].Entries[0].Values[0].Value);
        Assert.Equal("surname", (string?)result[1].Entries[0].Values[1].Name);
        Assert.Equal("Woolf", (string?)result[1].Entries[0].Values[1].Value);
        Assert.Equal("1526985685298-0", (string?)result[1].Entries[1].Id);
        Assert.Equal("name", (string?)result[1].Entries[1].Values[0].Name);
        Assert.Equal("Jane", (string?)result[1].Entries[1].Values[0].Value);
        Assert.Equal("surname", (string?)result[1].Entries[1].Values[1].Name);
        Assert.Equal("Austen", (string?)result[1].Entries[1].Values[1].Value);
    }

    // XREADGROUP tests - same format as XREAD (uses the same handler)
    // RESP2: Array reply with [stream_name, array of entries]
    // RESP3: Map reply with key-value pairs
    [Theory]
    [InlineData(RedisProtocol.Resp2, "*1\r\n*2\r\n$8\r\nmystream\r\n*1\r\n*2\r\n$3\r\n1-0\r\n*2\r\n$7\r\nmyfield\r\n$6\r\nmydata\r\n")]
    [InlineData(RedisProtocol.Resp3, "%1\r\n$8\r\nmystream\r\n*1\r\n*2\r\n$3\r\n1-0\r\n*2\r\n$7\r\nmyfield\r\n$6\r\nmydata\r\n")]
    public void MultiStreamProcessor_XReadGroup_SingleStreamSingleEntry(RedisProtocol protocol, string resp)
    {
        // XREADGROUP GROUP mygroup myconsumer STREAMS mystream >
        // 1) 1) "mystream"
        //    2) 1) 1) "1-0"
        //          2) 1) "myfield"
        //             2) "mydata"
        // the protocol is no longer an input: the handler reads the shape off the reply's own prefix
        Log($"{protocol}");
        var result = Execute(resp, Handler);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("mystream", (string?)result[0].Key);
        Assert.Single(result[0].Entries);
        Assert.Equal("1-0", (string?)result[0].Entries[0].Id);
        Assert.Single(result[0].Entries[0].Values);
        Assert.Equal("myfield", (string?)result[0].Entries[0].Values[0].Name);
        Assert.Equal("mydata", (string?)result[0].Entries[0].Values[0].Value);
    }

    [Theory]
    [InlineData(RedisProtocol.Resp2, "*1\r\n*2\r\n$8\r\nmystream\r\n*1\r\n*2\r\n$3\r\n1-0\r\n*-1\r\n")]
    [InlineData(RedisProtocol.Resp3, "%1\r\n$8\r\nmystream\r\n*1\r\n*2\r\n$3\r\n1-0\r\n_\r\n")]
    public void MultiStreamProcessor_XReadGroup_PendingMessageWithNilValues(RedisProtocol protocol, string resp)
    {
        // XREADGROUP GROUP mygroup myconsumer STREAMS mystream 0
        // Reading pending messages returns nil for values if already acknowledged
        // 1) 1) "mystream"
        //    2) 1) 1) "1-0"
        //          2) (nil)
        // the protocol is no longer an input: the handler reads the shape off the reply's own prefix
        Log($"{protocol}");
        var result = Execute(resp, Handler);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("mystream", (string?)result[0].Key);
        Assert.Single(result[0].Entries);
        Assert.Equal("1-0", (string?)result[0].Entries[0].Id);
        Assert.Empty(result[0].Entries[0].Values); // nil becomes empty array
    }

    [Theory]
    [InlineData(RedisProtocol.Resp2, "*-1\r\n")]
    [InlineData(RedisProtocol.Resp3, "_\r\n")]
    public void MultiStreamProcessor_XReadGroup_Timeout(RedisProtocol protocol, string resp)
    {
        // XREADGROUP with BLOCK that times out returns nil/null
        // the protocol is no longer an input: the handler reads the shape off the reply's own prefix
        Log($"{protocol}");
        var result = Execute(resp, Handler);

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}

/// <summary>
/// Reaches a command group's private handler, so the tests parse with the very instance the command sends.
/// </summary>
/// <remarks>
/// The groups keep their exotic handlers as private nested classes, which <c>InternalsVisibleTo</c> does not
/// reach; copying their one-line glue here would test the copy. Reflection fails loudly on a rename.
/// </remarks>
internal static class GroupHandlers
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Fetch a handler from a static property, field or method.</summary>
    /// <param name="group">The group, e.g. <c>Streams</c>.</param>
    /// <param name="nested">The nested handler class, or <see langword="null"/> for a member of the group itself.</param>
    /// <param name="member">The static property, field or method that hands out the handler.</param>
    /// <param name="args">Arguments, when <paramref name="member"/> is a method.</param>
    public static IRespHandler<T> Get<T>(Type group, string? nested, string member, params object?[] args)
    {
        var type = nested is null ? group : group.GetNestedType(nested, Any);
        Assert.NotNull(type);
        var value = type.GetProperty(member, Any)?.GetValue(null)
            ?? type.GetField(member, Any)?.GetValue(null)
            ?? type.GetMethod(member, Any)?.Invoke(null, args);
        return Assert.IsAssignableFrom<IRespHandler<T>>(value);
    }
}
