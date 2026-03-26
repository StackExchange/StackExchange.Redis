using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Unit tests for Condition subclasses using the RespReader path.
/// </summary>
public class ConditionTests(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    private static Message CreateConditionMessage(Condition condition, RedisCommand command, RedisKey key, params RedisValue[] values)
    {
        return values.Length switch
        {
            0 => Condition.ConditionProcessor.CreateMessage(condition, 0, CommandFlags.None, command, key),
            1 => Condition.ConditionProcessor.CreateMessage(condition, 0, CommandFlags.None, command, key, values[0]),
            2 => Condition.ConditionProcessor.CreateMessage(condition, 0, CommandFlags.None, command, key, values[0], values[1]),
            5 => Condition.ConditionProcessor.CreateMessage(condition, 0, CommandFlags.None, command, key, values[0], values[1], values[2], values[3], values[4]),
            _ => throw new System.NotSupportedException($"Unsupported value count: {values.Length}"),
        };
    }

    [Fact]
    public void ExistsCondition_KeyExists_True()
    {
        var condition = Condition.KeyExists("mykey");
        var message = CreateConditionMessage(condition, RedisCommand.EXISTS, "mykey");
        var result = Execute(":1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_KeyExists_False()
    {
        var condition = Condition.KeyExists("mykey");
        var message = CreateConditionMessage(condition, RedisCommand.EXISTS, "mykey");
        var result = Execute(":0\r\n", Condition.ConditionProcessor.Default, message);
        Assert.False(result);
    }

    [Fact]
    public void ExistsCondition_KeyNotExists_True()
    {
        var condition = Condition.KeyNotExists("mykey");
        var message = CreateConditionMessage(condition, RedisCommand.EXISTS, "mykey");
        var result = Execute(":0\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_KeyNotExists_False()
    {
        var condition = Condition.KeyNotExists("mykey");
        var message = CreateConditionMessage(condition, RedisCommand.EXISTS, "mykey");
        var result = Execute(":1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.False(result);
    }

    [Fact]
    public void ExistsCondition_HashExists_True()
    {
        var condition = Condition.HashExists("myhash", "field1");
        var message = CreateConditionMessage(condition, RedisCommand.HEXISTS, "myhash", "field1");
        var result = Execute(":1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_HashNotExists_True()
    {
        var condition = Condition.HashNotExists("myhash", "field1");
        var message = CreateConditionMessage(condition, RedisCommand.HEXISTS, "myhash", "field1");
        var result = Execute(":0\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_SetContains_True()
    {
        var condition = Condition.SetContains("myset", "member1");
        var message = CreateConditionMessage(condition, RedisCommand.SISMEMBER, "myset", "member1");
        var result = Execute(":1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_SetNotContains_True()
    {
        var condition = Condition.SetNotContains("myset", "member1");
        var message = CreateConditionMessage(condition, RedisCommand.SISMEMBER, "myset", "member1");
        var result = Execute(":0\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_SortedSetContains_True()
    {
        var condition = Condition.SortedSetContains("myzset", "member1");
        var message = CreateConditionMessage(condition, RedisCommand.ZSCORE, "myzset", "member1");
        var result = Execute("$1\r\n5\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_SortedSetContains_Null_False()
    {
        var condition = Condition.SortedSetContains("myzset", "member1");
        var message = CreateConditionMessage(condition, RedisCommand.ZSCORE, "myzset", "member1");
        var result = Execute("$-1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.False(result);
    }

    [Fact]
    public void ExistsCondition_SortedSetNotContains_True()
    {
        var condition = Condition.SortedSetNotContains("myzset", "member1");
        var message = CreateConditionMessage(condition, RedisCommand.ZSCORE, "myzset", "member1");
        var result = Execute("$-1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void StartsWithCondition_Match_True()
    {
        var condition = Condition.SortedSetContainsStarting("myzset", "pre");
        var message = CreateConditionMessage(condition, RedisCommand.ZRANGEBYLEX, "myzset", "[pre", "+", "LIMIT", 0, 1);
        var result = Execute("*1\r\n$6\r\nprefix\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void StartsWithCondition_NoMatch_False()
    {
        var condition = Condition.SortedSetContainsStarting("myzset", "pre");
        var message = CreateConditionMessage(condition, RedisCommand.ZRANGEBYLEX, "myzset", "[pre", "+", "LIMIT", 0, 1);
        var result = Execute("*0\r\n", Condition.ConditionProcessor.Default, message);
        Assert.False(result);
    }

    [Fact]
    public void StartsWithCondition_NotContainsStarting_True()
    {
        var condition = Condition.SortedSetNotContainsStarting("myzset", "pre");
        var message = CreateConditionMessage(condition, RedisCommand.ZRANGEBYLEX, "myzset", "[pre", "+", "LIMIT", 0, 1);
        var result = Execute("*0\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_StringEqual_True()
    {
        var condition = Condition.StringEqual("mykey", "value1");
        var message = CreateConditionMessage(condition, RedisCommand.GET, "mykey", RedisValue.Null);
        var result = Execute("$6\r\nvalue1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_StringEqual_False()
    {
        var condition = Condition.StringEqual("mykey", "value1");
        var message = CreateConditionMessage(condition, RedisCommand.GET, "mykey", RedisValue.Null);
        var result = Execute("$6\r\nvalue2\r\n", Condition.ConditionProcessor.Default, message);
        Assert.False(result);
    }

    [Fact]
    public void EqualsCondition_StringNotEqual_True()
    {
        var condition = Condition.StringNotEqual("mykey", "value1");
        var message = CreateConditionMessage(condition, RedisCommand.GET, "mykey", RedisValue.Null);
        var result = Execute("$6\r\nvalue2\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_HashEqual_True()
    {
        var condition = Condition.HashEqual("myhash", "field1", "value1");
        var message = CreateConditionMessage(condition, RedisCommand.HGET, "myhash", "field1");
        var result = Execute("$6\r\nvalue1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_HashNotEqual_True()
    {
        var condition = Condition.HashNotEqual("myhash", "field1", "value1");
        var message = CreateConditionMessage(condition, RedisCommand.HGET, "myhash", "field1");
        var result = Execute("$6\r\nvalue2\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_SortedSetEqual_True()
    {
        var condition = Condition.SortedSetEqual("myzset", "member1", 5.0);
        var message = CreateConditionMessage(condition, RedisCommand.ZSCORE, "myzset", "member1");
        var result = Execute("$1\r\n5\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_SortedSetEqual_False()
    {
        var condition = Condition.SortedSetEqual("myzset", "member1", 5.0);
        var message = CreateConditionMessage(condition, RedisCommand.ZSCORE, "myzset", "member1");
        var result = Execute("$1\r\n3\r\n", Condition.ConditionProcessor.Default, message);
        Assert.False(result);
    }

    [Fact]
    public void EqualsCondition_SortedSetNotEqual_True()
    {
        var condition = Condition.SortedSetNotEqual("myzset", "member1", 5.0);
        var message = CreateConditionMessage(condition, RedisCommand.ZSCORE, "myzset", "member1");
        var result = Execute("$1\r\n3\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void ListCondition_IndexEqual_True()
    {
        var condition = Condition.ListIndexEqual("mylist", 0, "value1");
        var message = CreateConditionMessage(condition, RedisCommand.LINDEX, "mylist", 0);
        var result = Execute("$6\r\nvalue1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void ListCondition_IndexEqual_False()
    {
        var condition = Condition.ListIndexEqual("mylist", 0, "value1");
        var message = CreateConditionMessage(condition, RedisCommand.LINDEX, "mylist", 0);
        var result = Execute("$6\r\nvalue2\r\n", Condition.ConditionProcessor.Default, message);
        Assert.False(result);
    }

    [Fact]
    public void ListCondition_IndexNotEqual_True()
    {
        var condition = Condition.ListIndexNotEqual("mylist", 0, "value1");
        var message = CreateConditionMessage(condition, RedisCommand.LINDEX, "mylist", 0);
        var result = Execute("$6\r\nvalue2\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void ListCondition_IndexExists_True()
    {
        var condition = Condition.ListIndexExists("mylist", 0);
        var message = CreateConditionMessage(condition, RedisCommand.LINDEX, "mylist", 0);
        var result = Execute("$6\r\nvalue1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void ListCondition_IndexExists_Null_False()
    {
        var condition = Condition.ListIndexExists("mylist", 0);
        var message = CreateConditionMessage(condition, RedisCommand.LINDEX, "mylist", 0);
        var result = Execute("$-1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.False(result);
    }

    [Fact]
    public void ListCondition_IndexNotExists_True()
    {
        var condition = Condition.ListIndexNotExists("mylist", 0);
        var message = CreateConditionMessage(condition, RedisCommand.LINDEX, "mylist", 0);
        var result = Execute("$-1\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_StringLengthEqual_True()
    {
        var condition = Condition.StringLengthEqual("mykey", 10);
        var message = CreateConditionMessage(condition, RedisCommand.STRLEN, "mykey");
        var result = Execute(":10\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_StringLengthEqual_False()
    {
        var condition = Condition.StringLengthEqual("mykey", 10);
        var message = CreateConditionMessage(condition, RedisCommand.STRLEN, "mykey");
        var result = Execute(":5\r\n", Condition.ConditionProcessor.Default, message);
        Assert.False(result);
    }

    [Fact]
    public void LengthCondition_StringLengthLessThan_True()
    {
        var condition = Condition.StringLengthLessThan("mykey", 10);
        var message = CreateConditionMessage(condition, RedisCommand.STRLEN, "mykey");
        var result = Execute(":5\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_StringLengthGreaterThan_True()
    {
        var condition = Condition.StringLengthGreaterThan("mykey", 10);
        var message = CreateConditionMessage(condition, RedisCommand.STRLEN, "mykey");
        var result = Execute(":15\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_HashLengthEqual_True()
    {
        var condition = Condition.HashLengthEqual("myhash", 5);
        var message = CreateConditionMessage(condition, RedisCommand.HLEN, "myhash");
        var result = Execute(":5\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_ListLengthEqual_True()
    {
        var condition = Condition.ListLengthEqual("mylist", 3);
        var message = CreateConditionMessage(condition, RedisCommand.LLEN, "mylist");
        var result = Execute(":3\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_SetLengthEqual_True()
    {
        var condition = Condition.SetLengthEqual("myset", 7);
        var message = CreateConditionMessage(condition, RedisCommand.SCARD, "myset");
        var result = Execute(":7\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_SortedSetLengthEqual_True()
    {
        var condition = Condition.SortedSetLengthEqual("myzset", 4);
        var message = CreateConditionMessage(condition, RedisCommand.ZCARD, "myzset");
        var result = Execute(":4\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_StreamLengthEqual_True()
    {
        var condition = Condition.StreamLengthEqual("mystream", 10);
        var message = CreateConditionMessage(condition, RedisCommand.XLEN, "mystream");
        var result = Execute(":10\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void SortedSetRangeLengthCondition_Equal_True()
    {
        var condition = Condition.SortedSetLengthEqual("myzset", 5, 0, 10);
        var message = CreateConditionMessage(condition, RedisCommand.ZCOUNT, "myzset", 0, 10);
        var result = Execute(":5\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void SortedSetRangeLengthCondition_LessThan_True()
    {
        var condition = Condition.SortedSetLengthLessThan("myzset", 10, 0, 100);
        var message = CreateConditionMessage(condition, RedisCommand.ZCOUNT, "myzset", 0, 100);
        var result = Execute(":5\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void SortedSetRangeLengthCondition_GreaterThan_True()
    {
        var condition = Condition.SortedSetLengthGreaterThan("myzset", 3, 0, 100);
        var message = CreateConditionMessage(condition, RedisCommand.ZCOUNT, "myzset", 0, 100);
        var result = Execute(":10\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void SortedSetScoreCondition_ScoreExists_True()
    {
        var condition = Condition.SortedSetScoreExists("myzset", 5.0);
        var message = CreateConditionMessage(condition, RedisCommand.ZCOUNT, "myzset", 5.0, 5.0);
        var result = Execute(":3\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }

    [Fact]
    public void SortedSetScoreCondition_ScoreExists_False()
    {
        var condition = Condition.SortedSetScoreExists("myzset", 5.0);
        var message = CreateConditionMessage(condition, RedisCommand.ZCOUNT, "myzset", 5.0, 5.0);
        var result = Execute(":0\r\n", Condition.ConditionProcessor.Default, message);
        Assert.False(result);
    }

    [Fact]
    public void SortedSetScoreCondition_ScoreNotExists_True()
    {
        var condition = Condition.SortedSetScoreNotExists("myzset", 5.0);
        var message = CreateConditionMessage(condition, RedisCommand.ZCOUNT, "myzset", 5.0, 5.0);
        var result = Execute(":0\r\n", Condition.ConditionProcessor.Default, message);
        Assert.True(result);
    }
}
