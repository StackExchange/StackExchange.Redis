using System;
using Xunit;

namespace StackExchange.Redis.Tests;

public partial class ResultProcessorUnitTests
{
    // SCAN/SSCAN format: array of 2 elements [cursor, array of keys]
    // Example: *2\r\n$1\r\n0\r\n*3\r\n$3\r\nkey1\r\n$3\r\nkey2\r\n$3\r\nkey3\r\n
    [Theory]
    [InlineData("*2\r\n$1\r\n0\r\n*0\r\n", 0L, 0)] // cursor 0, empty array
    [InlineData("*2\r\n$1\r\n5\r\n*0\r\n", 5L, 0)] // cursor 5, empty array
    [InlineData("*2\r\n$1\r\n0\r\n*1\r\n$3\r\nfoo\r\n", 0L, 1)] // cursor 0, 1 key
    [InlineData("*2\r\n$1\r\n0\r\n*3\r\n$3\r\nkey1\r\n$3\r\nkey2\r\n$3\r\nkey3\r\n", 0L, 3)] // cursor 0, 3 keys
    [InlineData("*2\r\n$2\r\n42\r\n*2\r\n$4\r\ntest\r\n$5\r\nhello\r\n", 42L, 2)] // cursor 42, 2 keys
    public void SetScanResultProcessor_ValidInput(string resp, long expectedCursor, int expectedCount)
    {
        var processor = RedisDatabase.SetScanResultProcessor.Default;
        var result = Execute(resp, processor);

        Assert.Equal(expectedCursor, result.Cursor);
        Assert.Equal(expectedCount, result.Count);
    }

    [Fact]
    public void SetScanResultProcessor_ValidatesContent()
    {
        // cursor 0, 3 keys: "key1", "key2", "key3"
        var resp = "*2\r\n$1\r\n0\r\n*3\r\n$4\r\nkey1\r\n$4\r\nkey2\r\n$4\r\nkey3\r\n";
        var processor = RedisDatabase.SetScanResultProcessor.Default;
        var result = Execute(resp, processor);

        Assert.Equal(0L, result.Cursor);
        Assert.Equal(3, result.Count);

        // Access the values through the result
        var values = result.Values;
        Assert.Equal(3, values.Length);
        Assert.Equal("key1", (string?)values[0]);
        Assert.Equal("key2", (string?)values[1]);
        Assert.Equal("key3", (string?)values[2]);

        result.Recycle();
    }

    // HSCAN format: array of 2 elements [cursor, interleaved array of field/value pairs]
    // Example: *2\r\n$1\r\n0\r\n*4\r\n$6\r\nfield1\r\n$6\r\nvalue1\r\n$6\r\nfield2\r\n$6\r\nvalue2\r\n
    [Theory]
    [InlineData("*2\r\n$1\r\n0\r\n*0\r\n", 0L, 0)] // cursor 0, empty array
    [InlineData("*2\r\n$1\r\n7\r\n*0\r\n", 7L, 0)] // cursor 7, empty array
    [InlineData("*2\r\n$1\r\n0\r\n*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n", 0L, 1)] // cursor 0, 1 pair
    [InlineData("*2\r\n$1\r\n0\r\n*4\r\n$2\r\nf1\r\n$2\r\nv1\r\n$2\r\nf2\r\n$2\r\nv2\r\n", 0L, 2)] // cursor 0, 2 pairs
    [InlineData("*2\r\n$2\r\n99\r\n*6\r\n$1\r\na\r\n$1\r\n1\r\n$1\r\nb\r\n$1\r\n2\r\n$1\r\nc\r\n$1\r\n3\r\n", 99L, 3)] // cursor 99, 3 pairs
    public void HashScanResultProcessor_ValidInput(string resp, long expectedCursor, int expectedCount)
    {
        var processor = RedisDatabase.HashScanResultProcessor.Default;
        var result = Execute(resp, processor);

        Assert.Equal(expectedCursor, result.Cursor);
        Assert.Equal(expectedCount, result.Count);
    }

    [Fact]
    public void HashScanResultProcessor_ValidatesContent()
    {
        // cursor 0, 2 pairs: "field1"="value1", "field2"="value2"
        var resp = "*2\r\n$1\r\n0\r\n*4\r\n$6\r\nfield1\r\n$6\r\nvalue1\r\n$6\r\nfield2\r\n$6\r\nvalue2\r\n";
        var processor = RedisDatabase.HashScanResultProcessor.Default;
        var result = Execute(resp, processor);

        Assert.Equal(0L, result.Cursor);
        Assert.Equal(2, result.Count);

        var entries = result.Values;
        Assert.Equal(2, entries.Length);
        Assert.Equal("field1", (string?)entries[0].Name);
        Assert.Equal("value1", (string?)entries[0].Value);
        Assert.Equal("field2", (string?)entries[1].Name);
        Assert.Equal("value2", (string?)entries[1].Value);

        result.Recycle();
    }

    // ZSCAN format: array of 2 elements [cursor, interleaved array of member/score pairs]
    // Example: *2\r\n$1\r\n0\r\n*4\r\n$7\r\nmember1\r\n$3\r\n1.5\r\n$7\r\nmember2\r\n$3\r\n2.5\r\n
    [Theory]
    [InlineData("*2\r\n$1\r\n0\r\n*0\r\n", 0L, 0)] // cursor 0, empty array
    [InlineData("*2\r\n$2\r\n10\r\n*0\r\n", 10L, 0)] // cursor 10, empty array
    [InlineData("*2\r\n$1\r\n0\r\n*2\r\n$3\r\nfoo\r\n$1\r\n1\r\n", 0L, 1)] // cursor 0, 1 pair
    [InlineData("*2\r\n$1\r\n0\r\n*4\r\n$2\r\nm1\r\n$3\r\n1.5\r\n$2\r\nm2\r\n$3\r\n2.5\r\n", 0L, 2)] // cursor 0, 2 pairs
    [InlineData("*2\r\n$2\r\n88\r\n*6\r\n$1\r\na\r\n$1\r\n1\r\n$1\r\nb\r\n$1\r\n2\r\n$1\r\nc\r\n$1\r\n3\r\n", 88L, 3)] // cursor 88, 3 pairs
    public void SortedSetScanResultProcessor_ValidInput(string resp, long expectedCursor, int expectedCount)
    {
        var processor = RedisDatabase.SortedSetScanResultProcessor.Default;
        var result = Execute(resp, processor);

        Assert.Equal(expectedCursor, result.Cursor);
        Assert.Equal(expectedCount, result.Count);
    }

    [Fact]
    public void SortedSetScanResultProcessor_ValidatesContent()
    {
        // cursor 0, 2 pairs: "member1"=1.5, "member2"=2.5
        var resp = "*2\r\n$1\r\n0\r\n*4\r\n$7\r\nmember1\r\n$3\r\n1.5\r\n$7\r\nmember2\r\n$3\r\n2.5\r\n";
        var processor = RedisDatabase.SortedSetScanResultProcessor.Default;
        var result = Execute(resp, processor);

        Assert.Equal(0L, result.Cursor);
        Assert.Equal(2, result.Count);

        var entries = result.Values;
        Assert.Equal(2, entries.Length);
        Assert.Equal("member1", (string?)entries[0].Element);
        Assert.Equal(1.5, entries[0].Score);
        Assert.Equal("member2", (string?)entries[1].Element);
        Assert.Equal(2.5, entries[1].Score);

        result.Recycle();
    }

    [Theory]
    [InlineData("*1\r\n$1\r\n0\r\n")] // only 1 element instead of 2
    [InlineData("*3\r\n$1\r\n0\r\n*0\r\n$4\r\nextra\r\n")] // 3 elements instead of 2
    [InlineData("$1\r\n0\r\n")] // scalar instead of array
    public void ScanProcessors_InvalidFormat(string resp)
    {
        ExecuteUnexpected(resp, RedisDatabase.SetScanResultProcessor.Default, caller: nameof(RedisDatabase.SetScanResultProcessor));
        ExecuteUnexpected(resp, RedisDatabase.HashScanResultProcessor.Default, caller: nameof(RedisDatabase.HashScanResultProcessor));
        ExecuteUnexpected(resp, RedisDatabase.SortedSetScanResultProcessor.Default, caller: nameof(RedisDatabase.SortedSetScanResultProcessor));
    }
}
