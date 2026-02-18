using System.Linq;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for ValuePairInterleavedProcessorBase and its derived processors.
/// These processors handle both interleaved (RESP2) and jagged (RESP3) formats.
/// </summary>
public partial class ResultProcessorUnitTests
{
    // HashEntryArrayProcessor tests
    [Theory]
    // RESP2 interleaved format: [key, value, key, value, ...]
    [InlineData("*4\r\n$3\r\nkey\r\n$5\r\nvalue\r\n$4\r\nkey2\r\n$6\r\nvalue2\r\n", "key=value,key2=value2")]
    [InlineData("*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a=b")]
    [InlineData("*0\r\n", "")]
    [InlineData("*-1\r\n", null)]
    // RESP3 map format (alternative aggregate type, still linear): %{key: value, key2: value2}
    [InlineData("%2\r\n$3\r\nkey\r\n$5\r\nvalue\r\n$4\r\nkey2\r\n$6\r\nvalue2\r\n", "key=value,key2=value2", RedisProtocol.Resp3)]
    [InlineData("%1\r\n$1\r\na\r\n$1\r\nb\r\n", "a=b", RedisProtocol.Resp3)]
    [InlineData("%0\r\n", "", RedisProtocol.Resp3)]
    [InlineData("_\r\n", null, RedisProtocol.Resp3)]
    // Jagged format (RESP3): [[key, value], [key, value], ...] - array of 2-element arrays
    [InlineData("*2\r\n*2\r\n$3\r\nkey\r\n$5\r\nvalue\r\n*2\r\n$4\r\nkey2\r\n$6\r\nvalue2\r\n", "key=value,key2=value2", RedisProtocol.Resp3)]
    [InlineData("*1\r\n*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a=b", RedisProtocol.Resp3)]
    [InlineData("*0\r\n", "", RedisProtocol.Resp3)]
    // RESP3 with attributes
    [InlineData(ATTRIB_FOO_BAR + "*4\r\n$3\r\nkey\r\n$5\r\nvalue\r\n$4\r\nkey2\r\n$6\r\nvalue2\r\n", "key=value,key2=value2")]
    public void HashEntryArray(string resp, string? expected, RedisProtocol protocol = RedisProtocol.Resp2)
    {
        var result = Execute(resp, ResultProcessor.HashEntryArray, protocol: protocol);
        if (expected == null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.NotNull(result);
            var formatted = string.Join(",", System.Linq.Enumerable.Select(result, e => $"{e.Name}={e.Value}"));
            Assert.Equal(expected, formatted);
        }
    }

    // SortedSetEntryArrayProcessor tests
    [Theory]
    // RESP2 interleaved format: [element, score, element, score, ...]
    [InlineData("*4\r\n$3\r\nfoo\r\n,1.5\r\n$3\r\nbar\r\n,2.5\r\n", "foo:1.5,bar:2.5")]
    [InlineData("*2\r\n$1\r\na\r\n,1.0\r\n", "a:1")]
    [InlineData("*0\r\n", "")]
    [InlineData("*-1\r\n", null)]
    // RESP3 map format (alternative aggregate type, still linear): %{element: score, element2: score2}
    [InlineData("%2\r\n$3\r\nfoo\r\n,1.5\r\n$3\r\nbar\r\n,2.5\r\n", "foo:1.5,bar:2.5", RedisProtocol.Resp3)]
    [InlineData("%1\r\n$1\r\na\r\n,1.0\r\n", "a:1", RedisProtocol.Resp3)]
    [InlineData("%0\r\n", "", RedisProtocol.Resp3)]
    [InlineData("_\r\n", null, RedisProtocol.Resp3)]
    // Jagged format (RESP3): [[element, score], [element, score], ...] - array of 2-element arrays
    [InlineData("*2\r\n*2\r\n$3\r\nfoo\r\n,1.5\r\n*2\r\n$3\r\nbar\r\n,2.5\r\n", "foo:1.5,bar:2.5", RedisProtocol.Resp3)]
    [InlineData("*1\r\n*2\r\n$1\r\na\r\n,1.0\r\n", "a:1", RedisProtocol.Resp3)]
    [InlineData("*0\r\n", "", RedisProtocol.Resp3)]
    // RESP3 with attributes
    [InlineData(ATTRIB_FOO_BAR + "*4\r\n$3\r\nfoo\r\n,1.5\r\n$3\r\nbar\r\n,2.5\r\n", "foo:1.5,bar:2.5")]
    public void SortedSetEntryArray(string resp, string? expected, RedisProtocol protocol = RedisProtocol.Resp2)
    {
        var result = Execute(resp, ResultProcessor.SortedSetWithScores, protocol: protocol);
        if (expected == null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.NotNull(result);
            var formatted = string.Join(",", System.Linq.Enumerable.Select<SortedSetEntry, string>(result, e => $"{e.Element}:{e.Score}"));
            Assert.Equal(expected, formatted);
        }
    }

    // StreamNameValueEntryProcessor tests
    [Theory]
    // RESP2 interleaved format: [name, value, name, value, ...]
    [InlineData("*4\r\n$4\r\nname\r\n$5\r\nvalue\r\n$5\r\nname2\r\n$6\r\nvalue2\r\n", "name=value,name2=value2")]
    [InlineData("*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a=b")]
    [InlineData("*0\r\n", "")]
    [InlineData("*-1\r\n", null)]
    // RESP3 map format (alternative aggregate type, still linear): %{name: value, name2: value2}
    [InlineData("%2\r\n$4\r\nname\r\n$5\r\nvalue\r\n$5\r\nname2\r\n$6\r\nvalue2\r\n", "name=value,name2=value2", RedisProtocol.Resp3)]
    [InlineData("%1\r\n$1\r\na\r\n$1\r\nb\r\n", "a=b", RedisProtocol.Resp3)]
    [InlineData("%0\r\n", "", RedisProtocol.Resp3)]
    [InlineData("_\r\n", null, RedisProtocol.Resp3)]
    // Jagged format (RESP3): [[name, value], [name, value], ...] - array of 2-element arrays
    [InlineData("*2\r\n*2\r\n$4\r\nname\r\n$5\r\nvalue\r\n*2\r\n$5\r\nname2\r\n$6\r\nvalue2\r\n", "name=value,name2=value2", RedisProtocol.Resp3)]
    [InlineData("*1\r\n*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a=b", RedisProtocol.Resp3)]
    [InlineData("*0\r\n", "", RedisProtocol.Resp3)]
    // RESP3 with attributes
    [InlineData(ATTRIB_FOO_BAR + "*4\r\n$4\r\nname\r\n$5\r\nvalue\r\n$5\r\nname2\r\n$6\r\nvalue2\r\n", "name=value,name2=value2")]
    public void StreamNameValueEntry(string resp, string? expected, RedisProtocol protocol = RedisProtocol.Resp2)
    {
        var result = Execute(resp, ResultProcessor.StreamNameValueEntryProcessor.Instance, protocol: protocol);
        if (expected == null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.NotNull(result);
            var formatted = string.Join(",", System.Linq.Enumerable.Select(result, e => $"{e.Name}={e.Value}"));
            Assert.Equal(expected, formatted);
        }
    }

    // StringPairInterleavedProcessor tests
    [Theory]
    // RESP2 interleaved format: [key, value, key, value, ...]
    [InlineData("*4\r\n$3\r\nkey\r\n$5\r\nvalue\r\n$4\r\nkey2\r\n$6\r\nvalue2\r\n", "key=value,key2=value2")]
    [InlineData("*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a=b")]
    [InlineData("*0\r\n", "")]
    [InlineData("*-1\r\n", null)]
    // RESP3 map format (alternative aggregate type, still linear): %{key: value, key2: value2}
    [InlineData("%2\r\n$3\r\nkey\r\n$5\r\nvalue\r\n$4\r\nkey2\r\n$6\r\nvalue2\r\n", "key=value,key2=value2", RedisProtocol.Resp3)]
    [InlineData("%1\r\n$1\r\na\r\n$1\r\nb\r\n", "a=b", RedisProtocol.Resp3)]
    [InlineData("%0\r\n", "", RedisProtocol.Resp3)]
    [InlineData("_\r\n", null, RedisProtocol.Resp3)]
    // Jagged format (RESP3): [[key, value], [key, value], ...] - array of 2-element arrays
    [InlineData("*2\r\n*2\r\n$3\r\nkey\r\n$5\r\nvalue\r\n*2\r\n$4\r\nkey2\r\n$6\r\nvalue2\r\n", "key=value,key2=value2", RedisProtocol.Resp3)]
    [InlineData("*1\r\n*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a=b", RedisProtocol.Resp3)]
    [InlineData("*0\r\n", "", RedisProtocol.Resp3)]
    // RESP3 with attributes
    [InlineData(ATTRIB_FOO_BAR + "*4\r\n$3\r\nkey\r\n$5\r\nvalue\r\n$4\r\nkey2\r\n$6\r\nvalue2\r\n", "key=value,key2=value2")]
    public void StringPairInterleaved(string resp, string? expected, RedisProtocol protocol = RedisProtocol.Resp2)
    {
        var result = Execute(resp, ResultProcessor.StringPairInterleaved, protocol: protocol);
        if (expected == null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.NotNull(result);
            var formatted = string.Join(",", System.Linq.Enumerable.Select(result, kvp => $"{kvp.Key}={kvp.Value}"));
            Assert.Equal(expected, formatted);
        }
    }

    // Failing tests - non-array inputs
    [Theory]
    [InlineData(":42\r\n")]
    [InlineData("$3\r\nfoo\r\n")]
    [InlineData("+OK\r\n")]
    public void FailingHashEntryArray(string resp) => ExecuteUnexpected(resp, ResultProcessor.HashEntryArray);

    [Theory]
    [InlineData(":42\r\n")]
    [InlineData("$3\r\nfoo\r\n")]
    [InlineData("+OK\r\n")]
    public void FailingSortedSetEntryArray(string resp) => ExecuteUnexpected(resp, ResultProcessor.SortedSetWithScores);

    // Malformed jagged arrays (inner arrays not exactly length 2)
    // Uniform odd counts: IsAllJaggedPairsReader returns false, falls back to interleaved, processes even pairs (discards odd element)
    // Mixed lengths: IsAllJaggedPairsReader returns false, falls back to interleaved, throws when trying to read arrays as scalars

    // HashEntry tests that succeed with malformed data (uniform odd counts - not detected as jagged, processes as interleaved)
    [Theory]
    [InlineData("*1\r\n*1\r\n$1\r\na\r\n", RedisProtocol.Resp3)] // Inner array has 1 element - uniform, processes 0 pairs (1 >> 1 = 0)
    [InlineData("*1\r\n*3\r\n$1\r\na\r\n$1\r\nb\r\n$1\r\nc\r\n", RedisProtocol.Resp3)] // Inner array has 3 elements - uniform, processes 1 pair (3 >> 1 = 1), discards 'c'
    public void HashEntryArrayMalformedJaggedSucceeds(string resp, RedisProtocol protocol)
    {
        var result = Execute(resp, ResultProcessor.HashEntryArray, protocol: protocol);
        Log($"Malformed jagged (uniform) result: {(result == null ? "null" : string.Join(",", result.Select(static e => $"{e.Name}={e.Value}")))}");
    }

    // HashEntry tests that throw (mixed lengths - fallback to interleaved tries to read arrays as scalars)
    [Theory]
    [InlineData("*2\r\n*2\r\n$1\r\na\r\n$1\r\nb\r\n*1\r\n$1\r\nc\r\n", RedisProtocol.Resp3)] // Mixed: first has 2, second has 1
    [InlineData("*2\r\n*2\r\n$1\r\na\r\n$1\r\nb\r\n*3\r\n$1\r\nc\r\n$1\r\nd\r\n$1\r\ne\r\n", RedisProtocol.Resp3)] // Mixed: first has 2, second has 3
    public void HashEntryArrayMalformedJaggedThrows(string resp, RedisProtocol protocol)
    {
        var ex = Assert.Throws<System.InvalidOperationException>(() => Execute(resp, ResultProcessor.HashEntryArray, protocol: protocol));
        Log($"Malformed jagged threw: {ex.GetType().Name}: {ex.Message}");
    }

    // SortedSetEntry tests that succeed with malformed data (uniform odd counts - not detected as jagged, processes as interleaved)
    [Theory]
    [InlineData("*1\r\n*1\r\n$1\r\na\r\n", RedisProtocol.Resp3)] // Inner array has 1 element - uniform, processes 0 pairs (1 >> 1 = 0)
    [InlineData("*1\r\n*3\r\n$1\r\na\r\n,1.0\r\n$1\r\nb\r\n", RedisProtocol.Resp3)] // Inner array has 3 elements - uniform, processes 1 pair (3 >> 1 = 1), discards 'b'
    public void SortedSetEntryArrayMalformedJaggedSucceeds(string resp, RedisProtocol protocol)
    {
        var result = Execute(resp, ResultProcessor.SortedSetWithScores, protocol: protocol);
        Log($"Malformed jagged (uniform) result: {(result == null ? "null" : string.Join(",", result.Select(static e => $"{e.Element}:{e.Score}")))}");
    }

    // SortedSetEntry tests that throw (mixed lengths - fallback to interleaved tries to read arrays as scalars)
    [Theory]
    [InlineData("*2\r\n*2\r\n$1\r\na\r\n,1.0\r\n*1\r\n$1\r\nb\r\n", RedisProtocol.Resp3)] // Mixed: first has 2, second has 1
    [InlineData("*2\r\n*2\r\n$1\r\na\r\n,1.0\r\n*3\r\n$1\r\nb\r\n,2.0\r\n$1\r\nc\r\n", RedisProtocol.Resp3)] // Mixed: first has 2, second has 3
    public void SortedSetEntryArrayMalformedJaggedThrows(string resp, RedisProtocol protocol)
    {
        var ex = Assert.Throws<System.InvalidOperationException>(() => Execute(resp, ResultProcessor.SortedSetWithScores, protocol: protocol));
        Log($"Malformed jagged threw: {ex.GetType().Name}: {ex.Message}");
    }
}
