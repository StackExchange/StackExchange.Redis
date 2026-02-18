using System.Linq;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for basic aggregate result processors (arrays)
/// </summary>
public partial class ResultProcessorUnitTests
{
    [Theory]
    [InlineData("*3\r\n:1\r\n:2\r\n:3\r\n", "1,2,3")]
    [InlineData("*?\r\n:1\r\n:2\r\n:3\r\n.\r\n", "1,2,3")] // streaming aggregate
    [InlineData("*2\r\n,42\r\n,-99\r\n", "42,-99")]
    [InlineData("*0\r\n", "")]
    [InlineData("*?\r\n.\r\n", "")] // streaming empty aggregate
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n:10\r\n:20\r\n", "10,20")]
    public void Int64Array(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.Int64Array)));

    [Theory]
    [InlineData("*3\r\n$3\r\nfoo\r\n$3\r\nbar\r\n$3\r\nbaz\r\n", "foo,bar,baz")]
    [InlineData("*?\r\n$3\r\nfoo\r\n$3\r\nbar\r\n$3\r\nbaz\r\n.\r\n", "foo,bar,baz")] // streaming aggregate
    [InlineData("*2\r\n+hello\r\n+world\r\n", "hello,world")]
    [InlineData("*0\r\n", "")]
    [InlineData("*?\r\n.\r\n", "")] // streaming empty aggregate
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a,b")]
    public void StringArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.StringArray)));

    [Theory]
    [InlineData("*3\r\n:1\r\n:0\r\n:1\r\n", "True,False,True")]
    [InlineData("*?\r\n:1\r\n:0\r\n:1\r\n.\r\n", "True,False,True")] // streaming aggregate
    [InlineData("*2\r\n#t\r\n#f\r\n", "True,False")]
    [InlineData("*0\r\n", "")]
    [InlineData("*?\r\n.\r\n", "")] // streaming empty aggregate
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n:1\r\n:0\r\n", "True,False")]
    public void BooleanArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.BooleanArray)));

    [Theory]
    [InlineData("*3\r\n$3\r\nfoo\r\n$3\r\nbar\r\n$3\r\nbaz\r\n", "foo,bar,baz")]
    [InlineData("*?\r\n$3\r\nfoo\r\n$3\r\nbar\r\n$3\r\nbaz\r\n.\r\n", "foo,bar,baz")] // streaming aggregate
    [InlineData("*3\r\n$3\r\nfoo\r\n$-1\r\n$3\r\nbaz\r\n", "foo,,baz")] // null element in middle (RESP2)
    [InlineData("*3\r\n$3\r\nfoo\r\n_\r\n$3\r\nbaz\r\n", "foo,,baz")] // null element in middle (RESP3)
    [InlineData("*0\r\n", "")]
    [InlineData("*?\r\n.\r\n", "")] // streaming empty aggregate
    [InlineData("$3\r\nfoo\r\n", "foo")] // single bulk string treated as array
    [InlineData("$-1\r\n", "")] // null bulk string treated as empty array
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$1\r\na\r\n:1\r\n", "a,1")]
    public void RedisValueArray(string resp, string expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.RedisValueArray)));

    [Theory]
    [InlineData("*3\r\n$3\r\nfoo\r\n$-1\r\n$3\r\nbaz\r\n", "foo,,baz")] // null element in middle (RESP2)
    [InlineData("*3\r\n$3\r\nfoo\r\n_\r\n$3\r\nbaz\r\n", "foo,,baz")] // null element in middle (RESP3)
    [InlineData("*2\r\n$5\r\nhello\r\n$5\r\nworld\r\n", "hello,world")]
    [InlineData("*?\r\n$5\r\nhello\r\n$5\r\nworld\r\n.\r\n", "hello,world")] // streaming aggregate
    [InlineData("*0\r\n", "")]
    [InlineData("*?\r\n.\r\n", "")] // streaming empty aggregate
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a,b")]
    public void NullableStringArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.NullableStringArray)));

    [Theory]
    [InlineData("*3\r\n$3\r\nkey\r\n$4\r\nkey2\r\n$4\r\nkey3\r\n", "key,key2,key3")]
    [InlineData("*?\r\n$3\r\nkey\r\n$4\r\nkey2\r\n$4\r\nkey3\r\n.\r\n", "key,key2,key3")] // streaming aggregate
    [InlineData("*3\r\n$3\r\nkey\r\n$-1\r\n$4\r\nkey3\r\n", "key,(null),key3")] // null element in middle (RESP2)
    [InlineData("*3\r\n$3\r\nkey\r\n_\r\n$4\r\nkey3\r\n", "key,(null),key3")] // null element in middle (RESP3)
    [InlineData("*0\r\n", "")]
    [InlineData("*?\r\n.\r\n", "")] // streaming empty aggregate
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n", "foo,bar")]
    public void RedisKeyArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.RedisKeyArray)));

    [Theory]
    [InlineData("*2\r\n+foo\r\n:42\r\n", 42)]
    [InlineData($"{ATTRIB_FOO_BAR}*2\r\n+foo\r\n:42\r\n", 42)]
    [InlineData($"*2\r\n{ATTRIB_FOO_BAR}+foo\r\n:42\r\n", 42)]
    [InlineData($"*2\r\n+foo\r\n{ATTRIB_FOO_BAR}:42\r\n", 42)]
    public void PubSubNumSub(string resp, long expected) => Assert.Equal(expected, Execute(resp, ResultProcessor.PubSubNumSub));

    [Theory]
    [InlineData("*-1\r\n")]
    [InlineData("_\r\n")]
    [InlineData(":42\r\n")]
    [InlineData("$-1\r\n")]
    [InlineData("*3\r\n+foo\r\n:42\r\n+bar\r\n")]
    [InlineData("*4\r\n+foo\r\n:42\r\n+bar\r\n:6\r\n")]
    public void FailingPubSubNumSub(string resp) => ExecuteUnexpected(resp, ResultProcessor.PubSubNumSub);

    [Theory]
    [InlineData("*3\r\n,1.5\r\n,2.5\r\n,3.5\r\n", "1.5,2.5,3.5")]
    [InlineData("*?\r\n,1.5\r\n,2.5\r\n,3.5\r\n.\r\n", "1.5,2.5,3.5")] // streaming aggregate
    [InlineData("*3\r\n,1.5\r\n_\r\n,3.5\r\n", "1.5,,3.5")] // null element in middle (RESP3)
    [InlineData("*3\r\n,1.5\r\n$-1\r\n,3.5\r\n", "1.5,,3.5")] // null element in middle (RESP2)
    [InlineData("*0\r\n", "")]
    [InlineData("*?\r\n.\r\n", "")] // streaming empty aggregate
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n,1.1\r\n,2.2\r\n", "1.1,2.2")]
    public void NullableDoubleArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.NullableDoubleArray)));

    [Theory]
    [InlineData("*3\r\n:0\r\n:1\r\n:2\r\n", "ConditionNotMet,Success,Due")]
    [InlineData("*?\r\n:0\r\n:1\r\n:2\r\n.\r\n", "ConditionNotMet,Success,Due")] // streaming aggregate
    [InlineData("*2\r\n:1\r\n:2\r\n", "Success,Due")]
    [InlineData("*0\r\n", "")]
    [InlineData("*?\r\n.\r\n", "")] // streaming empty aggregate
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n:0\r\n:2\r\n", "ConditionNotMet,Due")]
    public void ExpireResultArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.ExpireResultArray)));

    [Theory]
    [InlineData("*3\r\n:1\r\n:-1\r\n:1\r\n", "Success,ConditionNotMet,Success")]
    [InlineData("*?\r\n:1\r\n:-1\r\n:1\r\n.\r\n", "Success,ConditionNotMet,Success")] // streaming aggregate
    [InlineData("*2\r\n:1\r\n:-1\r\n", "Success,ConditionNotMet")]
    [InlineData("*0\r\n", "")]
    [InlineData("*?\r\n.\r\n", "")] // streaming empty aggregate
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n:1\r\n:-1\r\n", "Success,ConditionNotMet")]
    public void PersistResultArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.PersistResultArray)));

    [Theory]
    [InlineData("*2\r\n$3\r\nfoo\r\n,1.5\r\n", "foo: 1.5")]
    [InlineData("*2\r\n$3\r\nbar\r\n,2.5\r\n", "bar: 2.5")]
    [InlineData("*-1\r\n", null)] // RESP2 null array
    [InlineData("_\r\n", null)] // RESP3 pure null
    [InlineData("*0\r\n", null)] // empty array (0 elements)
    [InlineData("*1\r\n$3\r\nfoo\r\n", null)] // array with 1 element
    [InlineData("*3\r\n$3\r\nfoo\r\n,1.5\r\n$3\r\nbar\r\n", "foo: 1.5")] // array with 3 elements - takes first 2
    [InlineData("*?\r\n.\r\n", null)] // RESP3 streaming empty aggregate (0 elements)
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$3\r\nbaz\r\n,3.5\r\n", "baz: 3.5")]
    public void SortedSetEntry(string resp, string? expected)
    {
        var result = Execute(resp, ResultProcessor.SortedSetEntry);
        if (expected == null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.NotNull(result);
            Assert.Equal(expected, $"{result.Value.Element}: {result.Value.Score}");
        }
    }

    [Theory]
    [InlineData(":42\r\n")]
    [InlineData("$3\r\nfoo\r\n")]
    [InlineData("$-1\r\n")] // null scalar should NOT be treated as null result
    public void FailingSortedSetEntry(string resp) => ExecuteUnexpected(resp, ResultProcessor.SortedSetEntry);

    [Theory]
    [InlineData("*2\r\n$3\r\nkey\r\n*2\r\n*2\r\n$3\r\nfoo\r\n,1.5\r\n*2\r\n$3\r\nbar\r\n,2.5\r\n", "key", "foo: 1.5, bar: 2.5")]
    [InlineData("*2\r\n$4\r\nkey2\r\n*1\r\n*2\r\n$3\r\nbaz\r\n,3.5\r\n", "key2", "baz: 3.5")]
    [InlineData("*2\r\n$4\r\nkey3\r\n*0\r\n", "key3", "")]
    [InlineData("*-1\r\n", null, null)]
    [InlineData("_\r\n", null, null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$3\r\nkey\r\n*1\r\n*2\r\n$1\r\na\r\n,1.0\r\n", "key", "a: 1")]
    public void SortedSetPopResult(string resp, string? key, string? values)
    {
        var result = Execute(resp, ResultProcessor.SortedSetPopResult);
        if (key == null)
        {
            Assert.True(result.IsNull);
        }
        else
        {
            Assert.False(result.IsNull);
            Assert.Equal(key, (string?)result.Key);
            var entries = string.Join(", ", result.Entries.Select(e => $"{e.Element}: {e.Score}"));
            Assert.Equal(values, entries);
        }
    }

    [Theory]
    [InlineData(":42\r\n")]
    [InlineData("$3\r\nfoo\r\n")]
    [InlineData("*1\r\n$3\r\nkey\r\n")]
    [InlineData("$-1\r\n")] // null scalar should NOT be treated as null result
    public void FailingSortedSetPopResult(string resp) => ExecuteUnexpected(resp, ResultProcessor.SortedSetPopResult);

    [Theory]
    [InlineData("*2\r\n$3\r\nkey\r\n*3\r\n$3\r\nfoo\r\n$3\r\nbar\r\n$3\r\nbaz\r\n", "key", "foo,bar,baz")]
    [InlineData("*2\r\n$4\r\nkey2\r\n*1\r\n$1\r\na\r\n", "key2", "a")]
    [InlineData("*2\r\n$4\r\nkey3\r\n*0\r\n", "key3", "")]
    [InlineData("*-1\r\n", null, null)]
    [InlineData("_\r\n", null, null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$3\r\nkey\r\n*2\r\n$1\r\nx\r\n$1\r\ny\r\n", "key", "x,y")]
    public void ListPopResult(string resp, string? key, string? values)
    {
        var result = Execute(resp, ResultProcessor.ListPopResult);
        if (key == null)
        {
            Assert.True(result.IsNull);
        }
        else
        {
            Assert.False(result.IsNull);
            Assert.Equal(key, (string?)result.Key);
            Assert.Equal(values, Join(result.Values));
        }
    }

    [Theory]
    [InlineData(":42\r\n")]
    [InlineData("$3\r\nfoo\r\n")]
    [InlineData("*1\r\n$3\r\nkey\r\n")]
    [InlineData("$-1\r\n")] // null scalar should NOT be treated as null result
    public void FailingListPopResult(string resp) => ExecuteUnexpected(resp, ResultProcessor.ListPopResult);

    [Theory]
    [InlineData("*3\r\n$3\r\nfoo\r\n$3\r\nbar\r\n$3\r\nbaz\r\n", "foo,bar,baz")]
    [InlineData("*?\r\n$3\r\nfoo\r\n$3\r\nbar\r\n$3\r\nbaz\r\n.\r\n", "foo,bar,baz")] // streaming aggregate
    [InlineData("*2\r\n+hello\r\n+world\r\n", "hello,world")]
    [InlineData("*0\r\n", "")]
    [InlineData("*?\r\n.\r\n", "")] // streaming empty aggregate
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a,b")]
    public void RedisChannelArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.RedisChannelArrayLiteral)));
}
