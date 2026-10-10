using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class Scan(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    // the page handlers SSCAN, HSCAN and ZSCAN send with; each answers a RespScanPage<T>
    private static readonly IRespHandler<RespScanPage<RedisValue>> SetScan
        = GroupHandlers.Get<RespScanPage<RedisValue>>(typeof(global::StackExchange.Redis.Sets), null, "ValueScanHandler");

    private static readonly IRespHandler<RespScanPage<HashEntry>> HashScan
        = GroupHandlers.Get<RespScanPage<HashEntry>>(typeof(global::StackExchange.Redis.Hashes), null, "HashScanHandler");

    private static readonly IRespHandler<RespScanPage<SortedSetEntry>> SortedSetScan
        = GroupHandlers.Get<RespScanPage<SortedSetEntry>>(typeof(global::StackExchange.Redis.SortedSets), null, "SortedSetScanHandler");

    // SCAN/SSCAN format: array of 2 elements [cursor, array of keys]
    // Example: *2\r\n$1\r\n0\r\n*3\r\n$3\r\nkey1\r\n$3\r\nkey2\r\n$3\r\nkey3\r\n
    [Theory]
    [InlineData("*2\r\n$1\r\n0\r\n*0\r\n", 0L, 0)] // cursor 0, empty array
    [InlineData("*2\r\n$1\r\n5\r\n*0\r\n", 5L, 0)] // cursor 5, empty array
    [InlineData("*2\r\n$1\r\n0\r\n*1\r\n$3\r\nfoo\r\n", 0L, 1)] // cursor 0, 1 key
    [InlineData("*2\r\n$1\r\n0\r\n*3\r\n$4\r\nkey1\r\n$4\r\nkey2\r\n$4\r\nkey3\r\n", 0L, 3)] // cursor 0, 3 keys
    [InlineData("*2\r\n$2\r\n42\r\n*2\r\n$4\r\ntest\r\n$5\r\nhello\r\n", 42L, 2)] // cursor 42, 2 keys
    public void SetScanHandler_ValidInput(string resp, long expectedCursor, int expectedCount)
    {
        using var result = Execute(resp, SetScan);

        Assert.Equal(expectedCursor, result.Cursor);
        Assert.Equal(expectedCount, result.Count);
    }

    [Fact]
    public void SetScanHandler_ValidatesContent()
    {
        // cursor 0, 3 keys: "key1", "key2", "key3"
        var resp = "*2\r\n$1\r\n0\r\n*3\r\n$4\r\nkey1\r\n$4\r\nkey2\r\n$4\r\nkey3\r\n";
        using var result = Execute(resp, SetScan);

        Assert.Equal(0L, result.Cursor);
        Assert.Equal(3, result.Count);

        // Access the values through the result
        var values = result.Items.Span;
        Assert.Equal(3, values.Length);
        Assert.Equal("key1", (string?)values[0]);
        Assert.Equal("key2", (string?)values[1]);
        Assert.Equal("key3", (string?)values[2]);
    }

    // HSCAN format: array of 2 elements [cursor, interleaved array of field/value pairs]
    // Example: *2\r\n$1\r\n0\r\n*4\r\n$6\r\nfield1\r\n$6\r\nvalue1\r\n$6\r\nfield2\r\n$6\r\nvalue2\r\n
    [Theory]
    [InlineData("*2\r\n$1\r\n0\r\n*0\r\n", 0L, 0)] // cursor 0, empty array
    [InlineData("*2\r\n$1\r\n7\r\n*0\r\n", 7L, 0)] // cursor 7, empty array
    [InlineData("*2\r\n$1\r\n0\r\n*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n", 0L, 1)] // cursor 0, 1 pair
    [InlineData("*2\r\n$1\r\n0\r\n*4\r\n$2\r\nf1\r\n$2\r\nv1\r\n$2\r\nf2\r\n$2\r\nv2\r\n", 0L, 2)] // cursor 0, 2 pairs
    [InlineData("*2\r\n$2\r\n99\r\n*6\r\n$1\r\na\r\n$1\r\n1\r\n$1\r\nb\r\n$1\r\n2\r\n$1\r\nc\r\n$1\r\n3\r\n", 99L, 3)] // cursor 99, 3 pairs
    public void HashScanHandler_ValidInput(string resp, long expectedCursor, int expectedCount)
    {
        using var result = Execute(resp, HashScan);

        Assert.Equal(expectedCursor, result.Cursor);
        Assert.Equal(expectedCount, result.Count);
    }

    [Fact]
    public void HashScanHandler_ValidatesContent()
    {
        // cursor 0, 2 pairs: "field1"="value1", "field2"="value2"
        var resp = "*2\r\n$1\r\n0\r\n*4\r\n$6\r\nfield1\r\n$6\r\nvalue1\r\n$6\r\nfield2\r\n$6\r\nvalue2\r\n";
        using var result = Execute(resp, HashScan);

        Assert.Equal(0L, result.Cursor);
        Assert.Equal(2, result.Count);

        var entries = result.Items.Span;
        Assert.Equal(2, entries.Length);
        Assert.Equal("field1", (string?)entries[0].Name);
        Assert.Equal("value1", (string?)entries[0].Value);
        Assert.Equal("field2", (string?)entries[1].Name);
        Assert.Equal("value2", (string?)entries[1].Value);
    }

    // ZSCAN format: array of 2 elements [cursor, interleaved array of member/score pairs]
    // Example: *2\r\n$1\r\n0\r\n*4\r\n$7\r\nmember1\r\n$3\r\n1.5\r\n$7\r\nmember2\r\n$3\r\n2.5\r\n
    [Theory]
    [InlineData("*2\r\n$1\r\n0\r\n*0\r\n", 0L, 0)] // cursor 0, empty array
    [InlineData("*2\r\n$2\r\n10\r\n*0\r\n", 10L, 0)] // cursor 10, empty array
    [InlineData("*2\r\n$1\r\n0\r\n*2\r\n$3\r\nfoo\r\n$1\r\n1\r\n", 0L, 1)] // cursor 0, 1 pair
    [InlineData("*2\r\n$1\r\n0\r\n*4\r\n$2\r\nm1\r\n$3\r\n1.5\r\n$2\r\nm2\r\n$3\r\n2.5\r\n", 0L, 2)] // cursor 0, 2 pairs
    [InlineData("*2\r\n$2\r\n88\r\n*6\r\n$1\r\na\r\n$1\r\n1\r\n$1\r\nb\r\n$1\r\n2\r\n$1\r\nc\r\n$1\r\n3\r\n", 88L, 3)] // cursor 88, 3 pairs
    public void SortedSetScanHandler_ValidInput(string resp, long expectedCursor, int expectedCount)
    {
        using var result = Execute(resp, SortedSetScan);

        Assert.Equal(expectedCursor, result.Cursor);
        Assert.Equal(expectedCount, result.Count);
    }

    [Fact]
    public void SortedSetScanHandler_ValidatesContent()
    {
        // cursor 0, 2 pairs: "member1"=1.5, "member2"=2.5
        var resp = "*2\r\n$1\r\n0\r\n*4\r\n$7\r\nmember1\r\n$3\r\n1.5\r\n$7\r\nmember2\r\n$3\r\n2.5\r\n";
        using var result = Execute(resp, SortedSetScan);

        Assert.Equal(0L, result.Cursor);
        Assert.Equal(2, result.Count);

        var entries = result.Items.Span;
        Assert.Equal(2, entries.Length);
        Assert.Equal("member1", (string?)entries[0].Element);
        Assert.Equal(1.5, entries[0].Score);
        Assert.Equal("member2", (string?)entries[1].Element);
        Assert.Equal(2.5, entries[1].Score);
    }

    [Theory]
    [InlineData("*1\r\n$1\r\n0\r\n")] // only 1 element instead of 2
    [InlineData("*3\r\n$1\r\n0\r\n*0\r\n$4\r\nextra\r\n")] // 3 elements instead of 2
    [InlineData("$1\r\n0\r\n")] // scalar instead of array
    public void ScanHandlers_InvalidFormat(string resp)
    {
        ExecuteUnexpected(resp, SetScan, caller: "SSCAN");
        ExecuteUnexpected(resp, HashScan, caller: "HSCAN");
        ExecuteUnexpected(resp, SortedSetScan, caller: "ZSCAN");
    }
}
