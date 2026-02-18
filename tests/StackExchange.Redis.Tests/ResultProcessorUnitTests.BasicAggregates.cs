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
}
