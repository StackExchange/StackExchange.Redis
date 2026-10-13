using System;
using RESPite.Messages;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Unit tests for Condition subclasses reading their check replies, as a transaction does.
/// </summary>
public class ConditionTests(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    /// <summary>
    /// What <c>RespTransaction</c>'s condition operation does with the check's reply: the condition held
    /// only if it could read the reply <i>and</i> the reply satisfied it.
    /// </summary>
    /// <remarks>
    /// The shipped <c>ConditionProcessor</c> read the reply via the <c>Message</c> that
    /// <c>CreateMessages</c> built; both are gone. Each condition now renders its own check
    /// (<c>RenderCheck</c>) and validates the reply (<c>TryValidate</c>), and the operation's
    /// <c>ParseFrame</c> combines them as <c>TryValidate(...) &amp;&amp; held</c>. A reply the condition cannot
    /// read is reported here as a throw, so a test cannot pass by mistaking "unreadable" for "false".
    /// </remarks>
    private sealed class ConditionCheck(Condition condition) : IRespHandler<bool>
    {
        public bool Parse(ref RespReader reader)
            => condition.TryValidate(ref reader, out var held)
                ? held
                : throw new InvalidOperationException("The condition could not read the reply.");
    }

    [Fact]
    public void ExistsCondition_KeyExists_True()
    {
        var condition = Condition.KeyExists("mykey");
        var result = Execute(":1\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_KeyExists_False()
    {
        var condition = Condition.KeyExists("mykey");
        var result = Execute(":0\r\n", new ConditionCheck(condition));
        Assert.False(result);
    }

    [Fact]
    public void ExistsCondition_KeyNotExists_True()
    {
        var condition = Condition.KeyNotExists("mykey");
        var result = Execute(":0\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_KeyNotExists_False()
    {
        var condition = Condition.KeyNotExists("mykey");
        var result = Execute(":1\r\n", new ConditionCheck(condition));
        Assert.False(result);
    }

    [Fact]
    public void ExistsCondition_HashExists_True()
    {
        var condition = Condition.HashExists("myhash", "field1");
        var result = Execute(":1\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_HashNotExists_True()
    {
        var condition = Condition.HashNotExists("myhash", "field1");
        var result = Execute(":0\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_SetContains_True()
    {
        var condition = Condition.SetContains("myset", "member1");
        var result = Execute(":1\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_SetNotContains_True()
    {
        var condition = Condition.SetNotContains("myset", "member1");
        var result = Execute(":0\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_SortedSetContains_True()
    {
        var condition = Condition.SortedSetContains("myzset", "member1");
        var result = Execute("$1\r\n5\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void ExistsCondition_SortedSetContains_Null_False()
    {
        var condition = Condition.SortedSetContains("myzset", "member1");
        var result = Execute("$-1\r\n", new ConditionCheck(condition));
        Assert.False(result);
    }

    [Fact]
    public void ExistsCondition_SortedSetNotContains_True()
    {
        var condition = Condition.SortedSetNotContains("myzset", "member1");
        var result = Execute("$-1\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void StartsWithCondition_Match_True()
    {
        var condition = Condition.SortedSetContainsStarting("myzset", "pre");
        var result = Execute("*1\r\n$6\r\nprefix\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void StartsWithCondition_NoMatch_False()
    {
        var condition = Condition.SortedSetContainsStarting("myzset", "pre");
        var result = Execute("*0\r\n", new ConditionCheck(condition));
        Assert.False(result);
    }

    [Fact]
    public void StartsWithCondition_NotContainsStarting_True()
    {
        var condition = Condition.SortedSetNotContainsStarting("myzset", "pre");
        var result = Execute("*0\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_StringEqual_True()
    {
        var condition = Condition.StringEqual("mykey", "value1");
        var result = Execute("$6\r\nvalue1\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_StringEqual_False()
    {
        var condition = Condition.StringEqual("mykey", "value1");
        var result = Execute("$6\r\nvalue2\r\n", new ConditionCheck(condition));
        Assert.False(result);
    }

    [Fact]
    public void EqualsCondition_StringNotEqual_True()
    {
        var condition = Condition.StringNotEqual("mykey", "value1");
        var result = Execute("$6\r\nvalue2\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_HashEqual_True()
    {
        var condition = Condition.HashEqual("myhash", "field1", "value1");
        var result = Execute("$6\r\nvalue1\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_HashNotEqual_True()
    {
        var condition = Condition.HashNotEqual("myhash", "field1", "value1");
        var result = Execute("$6\r\nvalue2\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_SortedSetEqual_True()
    {
        var condition = Condition.SortedSetEqual("myzset", "member1", 5.0);
        var result = Execute("$1\r\n5\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void EqualsCondition_SortedSetEqual_False()
    {
        var condition = Condition.SortedSetEqual("myzset", "member1", 5.0);
        var result = Execute("$1\r\n3\r\n", new ConditionCheck(condition));
        Assert.False(result);
    }

    [Fact]
    public void EqualsCondition_SortedSetNotEqual_True()
    {
        var condition = Condition.SortedSetNotEqual("myzset", "member1", 5.0);
        var result = Execute("$1\r\n3\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void ListCondition_IndexEqual_True()
    {
        var condition = Condition.ListIndexEqual("mylist", 0, "value1");
        var result = Execute("$6\r\nvalue1\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void ListCondition_IndexEqual_False()
    {
        var condition = Condition.ListIndexEqual("mylist", 0, "value1");
        var result = Execute("$6\r\nvalue2\r\n", new ConditionCheck(condition));
        Assert.False(result);
    }

    [Fact]
    public void ListCondition_IndexNotEqual_True()
    {
        var condition = Condition.ListIndexNotEqual("mylist", 0, "value1");
        var result = Execute("$6\r\nvalue2\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void ListCondition_IndexExists_True()
    {
        var condition = Condition.ListIndexExists("mylist", 0);
        var result = Execute("$6\r\nvalue1\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void ListCondition_IndexExists_Null_False()
    {
        var condition = Condition.ListIndexExists("mylist", 0);
        var result = Execute("$-1\r\n", new ConditionCheck(condition));
        Assert.False(result);
    }

    [Fact]
    public void ListCondition_IndexNotExists_True()
    {
        var condition = Condition.ListIndexNotExists("mylist", 0);
        var result = Execute("$-1\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_StringLengthEqual_True()
    {
        var condition = Condition.StringLengthEqual("mykey", 10);
        var result = Execute(":10\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_StringLengthEqual_False()
    {
        var condition = Condition.StringLengthEqual("mykey", 10);
        var result = Execute(":5\r\n", new ConditionCheck(condition));
        Assert.False(result);
    }

    [Fact]
    public void LengthCondition_StringLengthLessThan_True()
    {
        var condition = Condition.StringLengthLessThan("mykey", 10);
        var result = Execute(":5\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_StringLengthGreaterThan_True()
    {
        var condition = Condition.StringLengthGreaterThan("mykey", 10);
        var result = Execute(":15\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_HashLengthEqual_True()
    {
        var condition = Condition.HashLengthEqual("myhash", 5);
        var result = Execute(":5\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_ListLengthEqual_True()
    {
        var condition = Condition.ListLengthEqual("mylist", 3);
        var result = Execute(":3\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_SetLengthEqual_True()
    {
        var condition = Condition.SetLengthEqual("myset", 7);
        var result = Execute(":7\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_SortedSetLengthEqual_True()
    {
        var condition = Condition.SortedSetLengthEqual("myzset", 4);
        var result = Execute(":4\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void LengthCondition_StreamLengthEqual_True()
    {
        var condition = Condition.StreamLengthEqual("mystream", 10);
        var result = Execute(":10\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void SortedSetRangeLengthCondition_Equal_True()
    {
        var condition = Condition.SortedSetLengthEqual("myzset", 5, 0, 10);
        var result = Execute(":5\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void SortedSetRangeLengthCondition_LessThan_True()
    {
        var condition = Condition.SortedSetLengthLessThan("myzset", 10, 0, 100);
        var result = Execute(":5\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void SortedSetRangeLengthCondition_GreaterThan_True()
    {
        var condition = Condition.SortedSetLengthGreaterThan("myzset", 3, 0, 100);
        var result = Execute(":10\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void SortedSetScoreCondition_ScoreExists_True()
    {
        var condition = Condition.SortedSetScoreExists("myzset", 5.0);
        var result = Execute(":3\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }

    [Fact]
    public void SortedSetScoreCondition_ScoreExists_False()
    {
        var condition = Condition.SortedSetScoreExists("myzset", 5.0);
        var result = Execute(":0\r\n", new ConditionCheck(condition));
        Assert.False(result);
    }

    [Fact]
    public void SortedSetScoreCondition_ScoreNotExists_True()
    {
        var condition = Condition.SortedSetScoreNotExists("myzset", 5.0);
        var result = Execute(":0\r\n", new ConditionCheck(condition));
        Assert.True(result);
    }
}
