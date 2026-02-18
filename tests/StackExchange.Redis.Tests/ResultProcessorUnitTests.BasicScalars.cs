using System;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for basic scalar result processors (Int32, Int64, Double, Boolean, String, etc.)
/// </summary>
public partial class ResultProcessorUnitTests
{
    [Theory]
    [InlineData(":1\r\n", 1)]
    [InlineData("+1\r\n", 1)]
    [InlineData("$1\r\n1\r\n", 1)]
    [InlineData("$?\r\n;1\r\n1\r\n;0\r\n", 1)] // streaming string
    [InlineData(",1\r\n", 1)]
    [InlineData(ATTRIB_FOO_BAR + ":1\r\n", 1)]
    [InlineData(":-42\r\n", -42)]
    [InlineData("+-42\r\n", -42)]
    [InlineData("$3\r\n-42\r\n", -42)]
    [InlineData("$?\r\n;1\r\n-\r\n;2\r\n42\r\n;0\r\n", -42)] // streaming string
    [InlineData(",-42\r\n", -42)]
    public void Int32(string resp, int value) => Assert.Equal(value, Execute(resp, ResultProcessor.Int32));

    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData("$4\r\nPONG\r\n")]
    public void FailingInt32(string resp) => ExecuteUnexpected(resp, ResultProcessor.Int32);

    [Theory]
    [InlineData(":1\r\n", 1)]
    [InlineData("+1\r\n", 1)]
    [InlineData("$1\r\n1\r\n", 1)]
    [InlineData("$?\r\n;1\r\n1\r\n;0\r\n", 1)] // streaming string
    [InlineData(",1\r\n", 1)]
    [InlineData(ATTRIB_FOO_BAR + ":1\r\n", 1)]
    [InlineData(":-42\r\n", -42)]
    [InlineData("+-42\r\n", -42)]
    [InlineData("$3\r\n-42\r\n", -42)]
    [InlineData("$?\r\n;1\r\n-\r\n;2\r\n42\r\n;0\r\n", -42)] // streaming string
    [InlineData(",-42\r\n", -42)]
    public void Int64(string resp, long value) => Assert.Equal(value, Execute(resp, ResultProcessor.Int64));

    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData("$4\r\nPONG\r\n")]
    public void FailingInt64(string resp) => ExecuteUnexpected(resp, ResultProcessor.Int64);

    [Theory]
    [InlineData(":42\r\n", 42.0)]
    [InlineData("+3.14\r\n", 3.14)]
    [InlineData("$4\r\n3.14\r\n", 3.14)]
    [InlineData("$?\r\n;1\r\n3\r\n;3\r\n.14\r\n;0\r\n", 3.14)] // streaming string
    [InlineData(",3.14\r\n", 3.14)]
    [InlineData(ATTRIB_FOO_BAR + ",3.14\r\n", 3.14)]
    [InlineData(":-1\r\n", -1.0)]
    [InlineData("+inf\r\n", double.PositiveInfinity)]
    [InlineData(",inf\r\n", double.PositiveInfinity)]
    [InlineData("$4\r\n-inf\r\n", double.NegativeInfinity)]
    [InlineData("$?\r\n;2\r\n-i\r\n;2\r\nnf\r\n;0\r\n", double.NegativeInfinity)] // streaming string
    [InlineData(",-inf\r\n", double.NegativeInfinity)]
    [InlineData(",nan\r\n", double.NaN)]
    public void Double(string resp, double value) => Assert.Equal(value, Execute(resp, ResultProcessor.Double));

    [Theory]
    [InlineData("_\r\n", null)]
    [InlineData("$-1\r\n", null)]
    [InlineData(":42\r\n", 42L)]
    [InlineData("+42\r\n", 42L)]
    [InlineData("$2\r\n42\r\n", 42L)]
    [InlineData("$?\r\n;1\r\n4\r\n;1\r\n2\r\n;0\r\n", 42L)] // streaming string
    [InlineData(",42\r\n", 42L)]
    [InlineData(ATTRIB_FOO_BAR + ":42\r\n", 42L)]
    public void NullableInt64(string resp, long? value) => Assert.Equal(value, Execute(resp, ResultProcessor.NullableInt64));

    [Theory]
    [InlineData("*1\r\n:99\r\n", 99L)]
    [InlineData("*?\r\n:99\r\n.\r\n", 99L)] // streaming aggregate
    [InlineData("*1\r\n$-1\r\n", null)] // unit array with RESP2 null bulk string
    [InlineData("*1\r\n_\r\n", null)] // unit array with RESP3 null
    [InlineData(ATTRIB_FOO_BAR + "*1\r\n:99\r\n", 99L)]
    public void NullableInt64ArrayOfOne(string resp, long? value) => Assert.Equal(value, Execute(resp, ResultProcessor.NullableInt64));

    [Theory]
    [InlineData("*-1\r\n")] // null array
    [InlineData("*0\r\n")] // empty array
    [InlineData("*?\r\n.\r\n")] // streaming empty aggregate
    [InlineData("*2\r\n:1\r\n:2\r\n")] // two elements
    [InlineData("*?\r\n:1\r\n:2\r\n.\r\n")] // streaming aggregate with two elements
    public void FailingNullableInt64ArrayOfNonOne(string resp) => ExecuteUnexpected(resp, ResultProcessor.NullableInt64);

    [Theory]
    [InlineData("_\r\n", null)]
    [InlineData("$-1\r\n", null)]
    [InlineData(":42\r\n", 42.0)]
    [InlineData("+3.14\r\n", 3.14)]
    [InlineData("$4\r\n3.14\r\n", 3.14)]
    [InlineData("$?\r\n;1\r\n3\r\n;3\r\n.14\r\n;0\r\n", 3.14)] // streaming string
    [InlineData(",3.14\r\n", 3.14)]
    [InlineData(ATTRIB_FOO_BAR + ",3.14\r\n", 3.14)]
    public void NullableDouble(string resp, double? value) => Assert.Equal(value, Execute(resp, ResultProcessor.NullableDouble));

    [Theory]
    [InlineData("_\r\n", false)] // null = false
    [InlineData(":0\r\n", false)]
    [InlineData(":1\r\n", true)]
    [InlineData("#f\r\n", false)]
    [InlineData("#t\r\n", true)]
    [InlineData("+OK\r\n", true)]
    [InlineData(ATTRIB_FOO_BAR + ":1\r\n", true)]
    public void Boolean(string resp, bool value) => Assert.Equal(value, Execute(resp, ResultProcessor.Boolean));

    [Theory]
    [InlineData("*1\r\n:1\r\n", true)] // SCRIPT EXISTS returns array
    [InlineData("*?\r\n:1\r\n.\r\n", true)] // streaming aggregate
    [InlineData("*1\r\n:0\r\n", false)]
    [InlineData(ATTRIB_FOO_BAR + "*1\r\n:1\r\n", true)]
    public void BooleanArrayOfOne(string resp, bool value) => Assert.Equal(value, Execute(resp, ResultProcessor.Boolean));

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*?\r\n.\r\n")] // streaming empty aggregate
    [InlineData("*2\r\n:1\r\n:0\r\n")] // two elements
    [InlineData("*?\r\n:1\r\n:0\r\n.\r\n")] // streaming aggregate with two elements
    [InlineData("*1\r\n*1\r\n:1\r\n")] // nested array (not scalar)
    public void FailingBooleanArrayOfNonOne(string resp) => ExecuteUnexpected(resp, ResultProcessor.Boolean);

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("$?\r\n;2\r\nhe\r\n;3\r\nllo\r\n;0\r\n", "hello")] // streaming string
    [InlineData("$?\r\n;0\r\n", "")] // streaming empty string
    [InlineData("+world\r\n", "world")]
    [InlineData(":42\r\n", "42")]
    [InlineData("$-1\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "$3\r\nfoo\r\n", "foo")]
    public void String(string resp, string? value) => Assert.Equal(value, Execute(resp, ResultProcessor.String));

    [Theory]
    [InlineData("*1\r\n$3\r\nbar\r\n", "bar")]
    [InlineData("*?\r\n$3\r\nbar\r\n.\r\n", "bar")] // streaming aggregate
    [InlineData(ATTRIB_FOO_BAR + "*1\r\n$3\r\nbar\r\n", "bar")]
    public void StringArrayOfOne(string resp, string? value) => Assert.Equal(value, Execute(resp, ResultProcessor.String));

    [Theory]
    [InlineData("*-1\r\n")] // null array
    [InlineData("*0\r\n")] // empty array
    [InlineData("*?\r\n.\r\n")] // streaming empty aggregate
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // two elements
    [InlineData("*?\r\n$3\r\nfoo\r\n$3\r\nbar\r\n.\r\n")] // streaming aggregate with two elements
    [InlineData("*1\r\n*1\r\n$3\r\nfoo\r\n")] // nested array (not scalar)
    public void FailingStringArrayOfNonOne(string resp) => ExecuteUnexpected(resp, ResultProcessor.String);

    [Theory]
    [InlineData("+string\r\n", Redis.RedisType.String)]
    [InlineData("+hash\r\n", Redis.RedisType.Hash)]
    [InlineData("+zset\r\n", Redis.RedisType.SortedSet)]
    [InlineData("+set\r\n", Redis.RedisType.Set)]
    [InlineData("+list\r\n", Redis.RedisType.List)]
    [InlineData("+stream\r\n", Redis.RedisType.Stream)]
    [InlineData("+blah\r\n", Redis.RedisType.Unknown)]
    [InlineData("$-1\r\n", Redis.RedisType.None)]
    [InlineData("_\r\n", Redis.RedisType.None)]
    [InlineData("$0\r\n\r\n", Redis.RedisType.None)]
    [InlineData(ATTRIB_FOO_BAR + "$6\r\nstring\r\n", Redis.RedisType.String)]
    public void RedisType(string resp, RedisType value) => Assert.Equal(value, Execute(resp, ResultProcessor.RedisType));

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("$?\r\n;2\r\nhe\r\n;3\r\nllo\r\n;0\r\n", "hello")] // streaming string
    [InlineData("+world\r\n", "world")]
    [InlineData(":42\r\n", "42")]
    [InlineData("$-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "$3\r\nfoo\r\n", "foo")]
    public void ByteArray(string resp, string? expected)
    {
        var result = Execute(resp, ResultProcessor.ByteArray);
        if (expected is null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(result!));
        }
    }

    [Theory]
    [InlineData("*-1\r\n")] // null array
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // array
    public void FailingByteArray(string resp) => ExecuteUnexpected(resp, ResultProcessor.ByteArray);

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("$?\r\n;2\r\nhe\r\n;3\r\nllo\r\n;0\r\n", "hello")] // streaming string
    [InlineData("+world\r\n", "world")]
    [InlineData("$-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "$11\r\nclusterinfo\r\n", "clusterinfo")]
    // note that this test does not include a valid cluster nodes response
    public void ClusterNodesRaw(string resp, string? expected) => Assert.Equal(expected, Execute(resp, ResultProcessor.ClusterNodesRaw));

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // array
    public void FailingClusterNodesRaw(string resp) => ExecuteUnexpected(resp, ResultProcessor.ClusterNodesRaw);

    [Theory]
    [InlineData(":42\r\n", 42L)]
    [InlineData("+99\r\n", 99L)]
    [InlineData("$2\r\n10\r\n", 10L)]
    [InlineData("$?\r\n;1\r\n1\r\n;1\r\n0\r\n;0\r\n", 10L)] // streaming string
    [InlineData(",123\r\n", 123L)]
    [InlineData(ATTRIB_FOO_BAR + ":42\r\n", 42L)]
    public void Int64DefaultValue(string resp, long expected) => Assert.Equal(expected, Execute(resp, Int64DefaultValue999));

    [Theory]
    [InlineData("_\r\n", 999L)] // null returns default
    [InlineData("$-1\r\n", 999L)] // null returns default
    public void Int64DefaultValueNull(string resp, long expected) => Assert.Equal(expected, Execute(resp, Int64DefaultValue999));

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n:1\r\n:2\r\n")] // array
    [InlineData("+notanumber\r\n")] // invalid number
    public void FailingInt64DefaultValue(string resp) => ExecuteUnexpected(resp, Int64DefaultValue999);

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("$?\r\n;2\r\nhe\r\n;3\r\nllo\r\n;0\r\n", "hello")] // streaming string
    [InlineData("+world\r\n", "world")]
    [InlineData(":42\r\n", "42")]
    [InlineData("$-1\r\n", "(null)")]
    [InlineData("_\r\n", "(null)")]
    [InlineData(ATTRIB_FOO_BAR + "$3\r\nfoo\r\n", "foo")]
    public void RedisKey(string resp, string expected)
    {
        var result = Execute(resp, ResultProcessor.RedisKey);
        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // array
    public void FailingRedisKey(string resp) => ExecuteUnexpected(resp, ResultProcessor.RedisKey);

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("$?\r\n;2\r\nhe\r\n;3\r\nllo\r\n;0\r\n", "hello")] // streaming string
    [InlineData("+world\r\n", "world")]
    [InlineData(":42\r\n", "42")]
    [InlineData("$-1\r\n", "")]
    [InlineData("_\r\n", "")]
    [InlineData(",3.14\r\n", "3.14")]
    [InlineData(ATTRIB_FOO_BAR + "$3\r\nfoo\r\n", "foo")]
    public void RedisValue(string resp, string expected)
    {
        var result = Execute(resp, ResultProcessor.RedisValue);
        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // array
    public void FailingRedisValue(string resp) => ExecuteUnexpected(resp, ResultProcessor.RedisValue);

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("$?\r\n;2\r\nhe\r\n;3\r\nllo\r\n;0\r\n", "hello")] // streaming string
    [InlineData("+world\r\n", "world")]
    [InlineData("$-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "$10\r\ntiebreaker\r\n", "tiebreaker")]
    public void TieBreaker(string resp, string? expected) => Assert.Equal(expected, Execute(resp, ResultProcessor.TieBreaker));

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // array
    public void FailingTieBreaker(string resp) => ExecuteUnexpected(resp, ResultProcessor.TieBreaker);
}
