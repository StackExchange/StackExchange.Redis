using System;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class Streams(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    // MultiStreamProcessor tests - XREAD command
    // Format: array of [stream_name, array of entries]
    // Each entry is [id, array of name/value pairs]
    [Fact]
    public void MultiStreamProcessor_EmptyResult()
    {
        // Server returns nil when no data available
        var resp = "*-1\r\n";
        var processor = ResultProcessor.MultiStream;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void MultiStreamProcessor_EmptyArray()
    {
        // Server returns empty array
        var resp = "*0\r\n";
        var processor = ResultProcessor.MultiStream;
        var result = Execute(resp, processor);

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

        var processor = ResultProcessor.MultiStream;
        var result = Execute(resp, processor);

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
        // (see ResultProcessor.cs lines 2336-2358 for the redis-cli format)
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

        var processor = ResultProcessor.MultiStream;
        var result = Execute(resp, processor);

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
}
